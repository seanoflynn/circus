using System;
using System.Collections.Generic;
using System.Linq;
using Circus.TimeProviders;
using Circus.Util;

namespace Circus.OrderBook
{
    public class InMemoryOrderBook : IOrderBook
    {
        private readonly Security _security;
        private readonly ITimeProvider _timeProvider;

        private OrderBookStatus _status = OrderBookStatus.Closed;
        private long _nextSequenceNumber;
        private long? _lastTradedPrice;

        // Keyed and compared as tick counts (price / Security.TickSize) rather than decimal —
        // see InternalOrder for why.
        private readonly Dictionary<Side, SortedDictionary<long, SortedDictionary<long, InternalOrder>>> _working =
            new()
            {
                {Side.Buy, new(new DescendingComparer())},
                {Side.Sell, new()}
            };

        private readonly Dictionary<Side, SortedDictionary<long, SortedDictionary<long, InternalOrder>>> _stops =
            new()
            {
                {Side.Buy, new()},
                {Side.Sell, new(new DescendingComparer())}
            };

        private readonly Dictionary<string, InternalOrder> _orders = new();
        private readonly Dictionary<string, InternalOrder> _completedOrders = new();

        // every (companyId, clientOrderId) pair ever assigned by a client, permanently reserved -
        // used for per-client uniqueness checks, ownership enforcement, and Update/Cancel lookups
        private readonly Dictionary<(string CompanyId, string ClientOrderId), InternalOrder> _clientOrderIndex = new();

        private const int MaxClientOrderIdLength = 20;

        public InMemoryOrderBook(Security security, ITimeProvider timeProvider)
        {
            _security = security;
            _timeProvider = timeProvider;
        }

        private DateTime Now() => _timeProvider.GetCurrentTime();

        public Security Security => _security;
        public OrderBookStatus Status => _status;

        public IList<Level> GetLevels(Side side, int maxPrices)
        {
            return _working[side].Take(maxPrices)
                .Select(x => new Level(
                    ToDecimal(x.Key),
                    x.Value.Sum(y => y.Value.RemainingQuantity),
                    x.Value.Count))
                .ToList();
        }

        public IList<OrderBookEvent> Process(OrderBookAction action)
        {
            return action switch
            {
                CreateOrder create => CreateOrder(create.CompanyId, create.ClientOrderId, create.OrderValidity,
                    create.Side, create.Quantity, create.Price, create.TriggerPrice, create.MarketLimit,
                    create.GoodTilDate, create.SelfMatchPreventionId, create.SelfMatchPreventionInstruction),
                UpdateOrder update => UpdateOrder(update.CompanyId, update.ClientOrderId,
                    update.PreviousClientOrderId, update.Quantity, update.Price, update.TriggerPrice),
                CancelOrder cancel => CancelOrder(cancel.CompanyId, cancel.ClientOrderId, cancel.PreviousClientOrderId),
                UpdateStatus update => UpdateStatus(update.Status),
                _ => throw new ArgumentException("Unknown order book action")
            };
        }

        public IList<OrderBookEvent> CreateOrder(string companyId, string clientOrderId, OrderValidity validity, Side side,
            int quantity, decimal? price = null, decimal? triggerPrice = null, bool marketLimit = false,
            DateOnly? goodTilDate = null, string? selfMatchPreventionId = null,
            SelfMatchPreventionInstruction? selfMatchPreventionInstruction = null)
        {
            var type = price.HasValue ? OrderType.Limit : OrderType.Market;
            var status = OrderStatus.Working;

            if (triggerPrice.HasValue)
            {
                type = (type == OrderType.Market ? OrderType.StopMarket : OrderType.StopLimit);
                status = OrderStatus.Hidden;
            }
            else if (marketLimit && type == OrderType.Market)
            {
                type = OrderType.MarketLimit;
            }

            if (_status == OrderBookStatus.Closed)
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.MarketClosed);
            if (type == OrderType.Market && _status == OrderBookStatus.PreOpen)
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.MarketPreOpen);
            if (string.IsNullOrEmpty(clientOrderId))
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.ClientOrderIdRequired);
            if (clientOrderId.Length > MaxClientOrderIdLength)
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.ClientOrderIdTooLong);
            if (string.IsNullOrEmpty(companyId))
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.CompanyIdRequired);
            if (companyId.Length > MaxClientOrderIdLength)
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.CompanyIdTooLong);
            if (selfMatchPreventionId != null && selfMatchPreventionId.Length > MaxClientOrderIdLength)
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.SelfMatchPreventionIdTooLong);
            if (quantity < 1)
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.InvalidQuantity);
            if (!TryConvertToTicks(price, out var priceTicks))
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.InvalidPriceIncrement);
            if (!TryConvertToTicks(triggerPrice, out var triggerTicks))
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.InvalidPriceIncrement);
            if (triggerTicks != null && priceTicks != null && side == Side.Buy && priceTicks < triggerTicks)
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.TriggerPriceMustBeLessThanPrice);
            if (triggerTicks != null && priceTicks != null && side == Side.Sell && priceTicks > triggerTicks)
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.TriggerPriceMustBeGreaterThanPrice);
            if (triggerTicks != null && !_lastTradedPrice.HasValue)
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.NoLastTradedPrice);
            if (triggerTicks != null && side == Side.Buy && triggerTicks <= _lastTradedPrice)
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.TriggerPriceMustBeGreaterThanLastTradedPrice);
            if (triggerTicks != null && side == Side.Sell && triggerTicks >= _lastTradedPrice)
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.TriggerPriceMustBeLessThanLastTradedPrice);
            if (_clientOrderIndex.TryGetValue((companyId, clientOrderId), out var existingOrder))
            {
                return existingOrder.Status is OrderStatus.Working or OrderStatus.Hidden
                    ? RejectCreate(companyId, clientOrderId, OrderRejectedReason.OrderInBook)
                    : RejectCreate(companyId, clientOrderId, OrderRejectedReason.OrderIdAlreadyUsed);
            }

            if (type == OrderType.Market || type == OrderType.MarketLimit)
            {
                var protectionTicks = type == OrderType.MarketLimit ? 0 : _security.MarketOrderProtectionTicks;
                if(!TryGetLimitPrice(side, protectionTicks, out priceTicks))
                    return RejectCreate(companyId, clientOrderId, OrderRejectedReason.NoOrdersToMatchMarketOrder);
            }

            if (validity == OrderValidity.FillOrKill && !triggerTicks.HasValue &&
                !HasSufficientLiquidity(side, priceTicks!.Value, quantity, selfMatchPreventionId,
                    selfMatchPreventionInstruction))
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.InsufficientLiquidityForFillOrKill);
            if (validity == OrderValidity.GoodTilDate && !goodTilDate.HasValue)
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.GoodTilDateRequired);
            if (goodTilDate.HasValue && goodTilDate.Value < DateOnly.FromDateTime(Now()))
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.InvalidExpireDate);

            _nextSequenceNumber++;
            var order = new InternalOrder(_nextSequenceNumber, companyId, clientOrderId, _security, Now(), status,
                type, validity, side, quantity, priceTicks, triggerTicks, goodTilDate, selfMatchPreventionId,
                selfMatchPreventionInstruction);

            _orders.Add(order.ExchangeOrderId, order);
            _clientOrderIndex.Add((companyId, clientOrderId), order);
            var orders = (triggerTicks.HasValue ? _stops : _working);
            var newPriceTicks = (triggerTicks ?? priceTicks) ?? throw new Exception("error");
            orders[side].Add(newPriceTicks, _nextSequenceNumber, order);

            List<OrderBookEvent> events = new();
            events.Add(new CreateOrderConfirmed(_security, Now(), companyId, order.ToOrder()));
            events.AddRange(Match());

            if (order.Validity == OrderValidity.FillAndKill && order.Status == OrderStatus.Working)
                events.Add(CancelRemainder(order, OrderCancelledReason.FillAndKillNotFilled));

            return events;
        }

        private bool TryConvertToTicks(decimal? price, out long? ticks)
        {
            if (!price.HasValue)
            {
                ticks = null;
                return true;
            }

            var rawTicks = price.Value / _security.TickSize;
            var truncatedTicks = Math.Truncate(rawTicks);
            if (rawTicks != truncatedTicks)
            {
                ticks = null;
                return false;
            }

            ticks = (long) truncatedTicks;
            return true;
        }

        private decimal ToDecimal(long ticks) => ticks * _security.TickSize;

        private bool TryGetLimitPrice(Side side, int protectionTicks, out long? priceTicks)
        {
            priceTicks = null;
            var opposing = _working[side == Side.Buy ? Side.Sell : Side.Buy];
            if (!opposing.Any())
                return false;

            // set price as best offer + protection ticks for buy orders, best bid - protection ticks for sell orders
            // TODO: option to use best bid + protection tickets for buy orders, etc (eurex)
            priceTicks = opposing.First().Key + ((side == Side.Buy ? 1 : -1) * protectionTicks);
            return true;
        }

        // selfMatchPreventionId/selfMatchPreventionInstruction are the incoming order's own
        // fields. Walks resting orders in the same price/time priority order Match() would
        // actually consume them in: a self-matched order with CancelResting is simply skipped
        // (the incoming order keeps going, only the resting order would die), but with
        // CancelAggressor/CancelBoth the incoming order itself would be cancelled right there,
        // so nothing beyond that point can ever count - liquidity checking must stop dead,
        // not just exclude that one order's quantity and keep summing past it.
        private bool HasSufficientLiquidity(Side side, long priceTicks, int quantity, string? selfMatchPreventionId,
            SelfMatchPreventionInstruction? selfMatchPreventionInstruction)
        {
            var opposing = _working[side == Side.Buy ? Side.Sell : Side.Buy];
            var total = 0;
            foreach (var level in opposing)
            {
                var crosses = side == Side.Buy ? level.Key <= priceTicks : level.Key >= priceTicks;
                if (!crosses)
                    break;

                foreach (var (_, restingOrder) in level.Value)
                {
                    if (TryGetSelfMatchInstruction(restingOrder, selfMatchPreventionId,
                            selfMatchPreventionInstruction, out var instruction))
                    {
                        // total < quantity is guaranteed here - otherwise we'd have already
                        // returned true below before reaching this order
                        if (instruction != SelfMatchPreventionInstruction.CancelResting)
                            return false;

                        continue;
                    }

                    total += restingOrder.RemainingQuantity;
                    if (total >= quantity)
                        return true;
                }
            }

            return total >= quantity;
        }

        private OrderBookEvent CancelRemainder(InternalOrder order, OrderCancelledReason reason)
        {
            var previousClientOrderId = order.ClientOrderId;
            order.Cancel(Now());
            CompleteOrder(order);
            return new CancelOrderConfirmed(_security, Now(), order.CompanyId, order.ToOrder(), previousClientOrderId,
                reason);
        }

        public IList<OrderBookEvent> UpdateOrder(string companyId, string clientOrderId, string previousClientOrderId,
            int? quantity = null, decimal? price = null, decimal? triggerPrice = null)
        {
            if (_status == OrderBookStatus.Closed)
                return RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.MarketClosed);
            if (string.IsNullOrEmpty(clientOrderId))
                return RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.ClientOrderIdRequired);
            if (clientOrderId.Length > MaxClientOrderIdLength)
                return RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.ClientOrderIdTooLong);
            if (string.IsNullOrEmpty(companyId))
                return RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.CompanyIdRequired);
            if (companyId.Length > MaxClientOrderIdLength)
                return RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.CompanyIdTooLong);
            if (quantity == null && price == null && triggerPrice == null)
                return RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.NoChange);
            if (quantity != null && quantity < 1)
                return RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.InvalidQuantity);
            if (!TryConvertToTicks(price, out var priceTicks))
                return RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.InvalidPriceIncrement);
            if (!TryConvertToTicks(triggerPrice, out var triggerTicks))
                return RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.InvalidPriceIncrement);
            if (!_clientOrderIndex.TryGetValue((companyId, previousClientOrderId), out var order) ||
                order.ClientOrderId != previousClientOrderId)
                return RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.OrderNotInBook);
            if (order.Status is not (OrderStatus.Working or OrderStatus.Hidden))
                return RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.TooLateToCancel,
                    order.ExchangeOrderId);
            if (_clientOrderIndex.TryGetValue((companyId, clientOrderId), out var conflictingOrder))
            {
                return conflictingOrder.Status is OrderStatus.Working or OrderStatus.Hidden
                    ? RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.OrderInBook,
                        order.ExchangeOrderId)
                    : RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.OrderIdAlreadyUsed,
                        order.ExchangeOrderId);
            }

            if (order.Status == OrderStatus.Hidden)
            {
                var newTriggerTicks = triggerTicks ?? order.TriggerPrice;
                var newPriceTicks = priceTicks ?? order.Price;

                if (newTriggerTicks != null && newPriceTicks != null && order.Side == Side.Buy && newPriceTicks < newTriggerTicks)
                    return RejectUpdate(companyId, clientOrderId, previousClientOrderId,
                        OrderRejectedReason.TriggerPriceMustBeLessThanPrice, order.ExchangeOrderId);
                if (newTriggerTicks != null && newPriceTicks != null && order.Side == Side.Sell && newPriceTicks > newTriggerTicks)
                    return RejectUpdate(companyId, clientOrderId, previousClientOrderId,
                        OrderRejectedReason.TriggerPriceMustBeGreaterThanPrice, order.ExchangeOrderId);

                if (triggerTicks != null && order.Side == Side.Buy && triggerTicks <= _lastTradedPrice)
                    return RejectUpdate(companyId, clientOrderId, previousClientOrderId,
                        OrderRejectedReason.TriggerPriceMustBeGreaterThanLastTradedPrice, order.ExchangeOrderId);
                if (triggerTicks != null && order.Side == Side.Sell && triggerTicks >= _lastTradedPrice)
                    return RejectUpdate(companyId, clientOrderId, previousClientOrderId,
                        OrderRejectedReason.TriggerPriceMustBeLessThanLastTradedPrice, order.ExchangeOrderId);
            }
            else
            {
                // ignore trigger price if already triggered
                triggerTicks = null;
            }

            // TODO: can't update price on stop market order?

            if (quantity <= order.FilledQuantity)
            {
                order.Cancel(Now(), clientOrderId);
                _clientOrderIndex[(companyId, clientOrderId)] = order;
                CompleteOrder(order);

                return new List<OrderBookEvent>
                {
                    new CancelOrderConfirmed(_security, Now(), order.CompanyId, order.ToOrder(), previousClientOrderId,
                        OrderCancelledReason.UpdatedQuantityLowerThanFilledQuantity)
                };
            }

            var sequenceNumber = order.SequenceNumber;
            var isPriceChange = (triggerTicks != null && order.Status == OrderStatus.Hidden && triggerTicks != order.TriggerPrice) ||
                                (priceTicks != null && order.Status != OrderStatus.Hidden && priceTicks != order.Price);
            var isQuantityIncrease = (quantity != null && quantity > order.Quantity);

            var orders = (order.Status == OrderStatus.Hidden ? _stops : _working);

            if (isPriceChange || isQuantityIncrease)
            {
                _nextSequenceNumber++;
                sequenceNumber = _nextSequenceNumber;
                var currentPriceTicks = (order.Status == OrderStatus.Hidden ? order.TriggerPrice : order.Price) ??
                                   throw new InvalidOperationException("missing price");
                var updatedPriceTicks =
                    (order.Status == OrderStatus.Hidden ? triggerTicks ?? order.TriggerPrice : priceTicks ?? order.Price) ??
                    throw new InvalidOperationException("missing price");
                orders[order.Side].Remove(currentPriceTicks, order.SequenceNumber);
                orders[order.Side].Add(updatedPriceTicks, sequenceNumber, order);
            }
            order.Update(sequenceNumber, Now(), quantity, triggerTicks, priceTicks, clientOrderId);
            _clientOrderIndex[(companyId, clientOrderId)] = order;

            List<OrderBookEvent> events = new();
            events.Add(new UpdateOrderConfirmed(_security, Now(), order.CompanyId, order.ToOrder(), previousClientOrderId));
            events.AddRange(Match());
            return events;
        }

        public IList<OrderBookEvent> CancelOrder(string companyId, string clientOrderId, string previousClientOrderId)
        {
            if (_status == OrderBookStatus.Closed)
                return RejectCancel(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.MarketClosed);
            if (string.IsNullOrEmpty(clientOrderId))
                return RejectCancel(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.ClientOrderIdRequired);
            if (clientOrderId.Length > MaxClientOrderIdLength)
                return RejectCancel(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.ClientOrderIdTooLong);
            if (string.IsNullOrEmpty(companyId))
                return RejectCancel(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.CompanyIdRequired);
            if (companyId.Length > MaxClientOrderIdLength)
                return RejectCancel(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.CompanyIdTooLong);
            if (!_clientOrderIndex.TryGetValue((companyId, previousClientOrderId), out var order) ||
                order.ClientOrderId != previousClientOrderId)
                return RejectCancel(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.OrderNotInBook);
            if (order.Status is not (OrderStatus.Working or OrderStatus.Hidden))
                return RejectCancel(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.TooLateToCancel,
                    order.ExchangeOrderId);
            if (_clientOrderIndex.TryGetValue((companyId, clientOrderId), out var conflictingOrder))
            {
                return conflictingOrder.Status is OrderStatus.Working or OrderStatus.Hidden
                    ? RejectCancel(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.OrderInBook,
                        order.ExchangeOrderId)
                    : RejectCancel(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.OrderIdAlreadyUsed,
                        order.ExchangeOrderId);
            }

            order.Cancel(Now(), clientOrderId);
            _clientOrderIndex[(companyId, clientOrderId)] = order;
            CompleteOrder(order);

            return new List<OrderBookEvent>
            {
                new CancelOrderConfirmed(_security, Now(), order.CompanyId, order.ToOrder(), previousClientOrderId,
                    OrderCancelledReason.Cancelled)
            };
        }

        private List<OrderBookEvent> RejectCreate(string companyId, string clientOrderId, OrderRejectedReason reason) =>
            new() {new CreateOrderRejected(_security, Now(), companyId, clientOrderId, reason)};

        private List<OrderBookEvent> RejectUpdate(string companyId, string clientOrderId, string previousClientOrderId,
                OrderRejectedReason reason, string? exchangeOrderId = null) =>
            new()
            {
                new UpdateOrderRejected(_security, Now(), companyId, clientOrderId, previousClientOrderId,
                    exchangeOrderId, reason)
            };

        private List<OrderBookEvent> RejectCancel(string companyId, string clientOrderId, string previousClientOrderId,
                OrderRejectedReason reason, string? exchangeOrderId = null) =>
            new()
            {
                new CancelOrderRejected(_security, Now(), companyId, clientOrderId, previousClientOrderId,
                    exchangeOrderId, reason)
            };

        private OrderBookEvent ExpireOrder(InternalOrder order)
        {
            order.Expire(Now());
            CompleteOrder(order);

            return new ExpireOrderConfirmed(_security, Now(), order.CompanyId, order.ToOrder());
        }

        private void CompleteOrder(InternalOrder order)
        {
            if (order.Type == OrderType.StopLimit || order.Type == OrderType.StopMarket)
            {
                var price = order.TriggerPrice ?? throw new InvalidOperationException("stop order missing stop price");
                _stops[order.Side].Remove(price, order.SequenceNumber);
            }
            else
            {
                var price = order.Price ?? throw new InvalidOperationException("limit order missing price");
                _working[order.Side].Remove(price, order.SequenceNumber);
            }

            FinishOrder(order);
        }

        private void FinishOrder(InternalOrder order)
        {
            _orders.Remove(order.ExchangeOrderId);
            _completedOrders.Add(order.ExchangeOrderId, order);
        }

        private void FillOrder(InternalOrder order, DateTime time, int quantity)
        {
            order.Fill(time, quantity);
            if (order.Status == OrderStatus.Filled)
            {
                CompleteOrder(order);
            }
        }

        private IEnumerable<OrderBookEvent> Match()
        {
            if (_status != OrderBookStatus.Open)
            {
                return Array.Empty<OrderBookEvent>();
            }

            var events = new List<OrderBookEvent>();
            var time = Now();

            var buy = _working[Side.Buy].FirstOrDefault().Value?.FirstOrDefault().Value;
            var sell = _working[Side.Sell].FirstOrDefault().Value?.FirstOrDefault().Value;

            if (buy != null && !buy.Price.HasValue)
            {
                throw new InvalidOperationException("buy limit order requires price");
            }

            if (sell != null && !sell.Price.HasValue)
            {
                throw new InvalidOperationException("sell limit order requires price");
            }

            while (buy != null && sell != null && buy.Price >= sell.Price)
            {
                var resting = buy.ModifiedTime < sell.ModifiedTime ? buy : sell;
                var aggressor = buy == resting ? sell : buy;

                if (IsSelfMatch(resting, aggressor, out var instruction))
                {
                    if (instruction != SelfMatchPreventionInstruction.CancelAggressor)
                        events.Add(CancelRemainder(resting, OrderCancelledReason.SelfMatchPrevention));
                    if (instruction != SelfMatchPreventionInstruction.CancelResting)
                        events.Add(CancelRemainder(aggressor, OrderCancelledReason.SelfMatchPrevention));

                    buy = _working[Side.Buy].FirstOrDefault().Value?.FirstOrDefault().Value;
                    sell = _working[Side.Sell].FirstOrDefault().Value?.FirstOrDefault().Value;
                    continue;
                }

                var quantity = Math.Min(resting.RemainingQuantity, aggressor.RemainingQuantity);
                var priceTicks = resting.Price ?? throw new InvalidOperationException("limit order requires price");
                var price = ToDecimal(priceTicks);

                FillOrder(resting, time, quantity);
                FillOrder(aggressor, time, quantity);

                events.Add(new OrdersMatched(_security, time, price, quantity,
                    new[]
                    {
                        new FillOrderConfirmed(_security, time, resting.CompanyId, resting.ToOrder(), price, quantity,
                            true),
                        new FillOrderConfirmed(_security, time, aggressor.CompanyId, aggressor.ToOrder(), price,
                            quantity, false)
                    }
                ));

                if (_lastTradedPrice != priceTicks)
                {
                    _lastTradedPrice = priceTicks;
                    events.AddRange(CheckStops());
                }

                buy = _working[Side.Buy].FirstOrDefault().Value?.FirstOrDefault().Value;
                sell = _working[Side.Sell].FirstOrDefault().Value?.FirstOrDefault().Value;
            }

            return events;
        }

        // Two orders are a prevented self-match only if both carry the same non-null
        // SelfMatchPreventionId - matches CME/Eurex, where this is a dedicated opt-in id
        // distinct from the firm/company identifier (so unrelated desks under one company
        // aren't blocked from trading each other).
        private static bool IsSelfMatch(InternalOrder resting, InternalOrder aggressor,
            out SelfMatchPreventionInstruction instruction) =>
            TryGetSelfMatchInstruction(resting, aggressor.SelfMatchPreventionId,
                aggressor.SelfMatchPreventionInstruction, out instruction);

        private static bool TryGetSelfMatchInstruction(InternalOrder resting, string? incomingSelfMatchPreventionId,
            SelfMatchPreventionInstruction? incomingInstruction, out SelfMatchPreventionInstruction instruction)
        {
            if (incomingSelfMatchPreventionId == null ||
                resting.SelfMatchPreventionId != incomingSelfMatchPreventionId)
            {
                instruction = default;
                return false;
            }

            instruction = incomingInstruction ?? resting.SelfMatchPreventionInstruction ??
                SelfMatchPreventionInstruction.CancelResting;
            return true;
        }

        private IEnumerable<OrderBookEvent> CheckStops()
        {
            var time = Now();
            var triggered = new SortedDictionary<long, InternalOrder>();

            var buys = _stops[Side.Buy].FirstOrDefault();
            while (!buys.Equals(default(KeyValuePair<long, SortedDictionary<long, InternalOrder>>)) &&
                   buys.Key <= _lastTradedPrice)
            {
                foreach (var (seqNum, order) in buys.Value)
                {
                    triggered.Add(seqNum, order);
                }
                _stops[Side.Buy].Remove(buys.Key);
                buys = _stops[Side.Buy].FirstOrDefault();
            }

            var sells = _stops[Side.Sell].FirstOrDefault();
            while (!sells.Equals(default(KeyValuePair<long, SortedDictionary<long, InternalOrder>>)) &&
                   sells.Key >= _lastTradedPrice)
            {
                foreach (var (seqNum, order) in sells.Value)
                {
                    triggered.Add(seqNum, order);
                }
                _stops[Side.Sell].Remove(sells.Key);
                sells = _stops[Side.Sell].FirstOrDefault();
            }

            var events = new List<OrderBookEvent>();

            if (triggered.Any())
            {
                events.AddRange(TriggerStops(triggered, time));
                events.AddRange(Match());

                foreach (var order in triggered.Values)
                {
                    if (order.Validity == OrderValidity.FillAndKill && order.RemainingQuantity > 0)
                        events.Add(CancelRemainder(order, OrderCancelledReason.FillAndKillNotFilled));
                }
            }

            return events;
        }

        private IList<OrderBookEvent> TriggerStops(SortedDictionary<long, InternalOrder> orders, DateTime time)
        {
            var events = new List<OrderBookEvent>();

            foreach (var (_, order) in orders)
            {
                // calculate price for stop market orders
                long? newPriceTicks = order.Price;
                if (order.Type == OrderType.StopMarket &&
                    !TryGetLimitPrice(order.Side, _security.MarketOrderProtectionTicks, out newPriceTicks))
                {
                    var previousClientOrderId = order.ClientOrderId;
                    order.Cancel(Now());
                    FinishOrder(order);

                    events.Add(new CancelOrderConfirmed(_security, Now(), order.CompanyId, order.ToOrder(),
                        previousClientOrderId, OrderCancelledReason.NoOrdersToMatchMarketOrder));
                    continue;
                }

                if (order.Validity == OrderValidity.FillOrKill &&
                    !HasSufficientLiquidity(order.Side, newPriceTicks!.Value, order.RemainingQuantity,
                        order.SelfMatchPreventionId, order.SelfMatchPreventionInstruction))
                {
                    var previousClientOrderId = order.ClientOrderId;
                    order.Cancel(Now());
                    FinishOrder(order);

                    events.Add(new CancelOrderConfirmed(_security, Now(), order.CompanyId, order.ToOrder(),
                        previousClientOrderId, OrderCancelledReason.FillOrKillNotFilled));
                    continue;
                }

                _nextSequenceNumber++;
                order.ConvertToLimit(time, _nextSequenceNumber, newPriceTicks);

                var limitPriceTicks = order.Price ?? throw new Exception("missing price");
                _working[order.Side].Add(limitPriceTicks, order.SequenceNumber, order);

                events.Add(new UpdateOrderConfirmed(_security, time, order.CompanyId, order.ToOrder(),
                    order.ClientOrderId));
            }

            return events;
        }

        public IList<OrderBookEvent> UpdateStatus(OrderBookStatus status)
        {
            return status switch
            {
                OrderBookStatus.PreOpen => PreOpenMarket(),
                OrderBookStatus.Open => OpenMarket(),
                OrderBookStatus.Closed => CloseMarket(),
                _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
            };
        }

        private IList<OrderBookEvent> PreOpenMarket()
        {
            // TODO: need better system for multiple sessions per day
            var date = Now();
            _nextSequenceNumber = ((date.Year * 10000) + (date.Month * 100) + date.Day) * 10000000000L;
            _status = OrderBookStatus.PreOpen;
            return new List<OrderBookEvent> {new StatusChanged(_security, Now(), _status)};
        }

        private IList<OrderBookEvent> OpenMarket()
        {
            _status = OrderBookStatus.Open;
            var events = new List<OrderBookEvent> {new StatusChanged(_security, Now(), _status)};
            events.AddRange(Match());
            return events;
        }

        private IList<OrderBookEvent> CloseMarket()
        {
            _status = OrderBookStatus.Closed;
            var events = new List<OrderBookEvent> {new StatusChanged(_security, Now(), _status)};
            events.AddRange(ExpireOrders());
            return events;
        }

        private IEnumerable<OrderBookEvent> ExpireOrders()
        {
            var today = DateOnly.FromDateTime(Now());
            var orders = _orders.Values.Where(o =>
                o.Validity == OrderValidity.Day ||
                (o.Validity == OrderValidity.GoodTilDate && o.GoodTilDate <= today)).ToList();

            return orders.Select(ExpireOrder).ToList();
        }
    }

    public record Level(decimal Price, int Quantity, int Count);

    internal static class SortedDictionaryExtensions
    {
        internal static void Add(this SortedDictionary<long, SortedDictionary<long, InternalOrder>> orders,
            long price, long sequenceNumber, InternalOrder order)
        {
            if (orders.ContainsKey(price))
            {
                orders[price].Add(sequenceNumber, order);
            }
            else
            {
                orders[price] = new SortedDictionary<long, InternalOrder> {{sequenceNumber, order}};
            }
        }

        internal static void Remove(this SortedDictionary<long, SortedDictionary<long, InternalOrder>> orders,
            long price, long sequenceNumber)
        {
            orders[price].Remove(sequenceNumber);

            if (orders[price].Count == 0)
            {
                orders.Remove(price);
            }
        }
    }
}
