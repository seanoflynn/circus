using System;
using System.Collections.Generic;
using System.Linq;
using Circus.TimeProviders;

namespace Circus.OrderBook
{
    public class InMemoryOrderBook : IOrderBook
    {
        private readonly Security _security;
        private readonly ITimeProvider _timeProvider;

        private OrderBookStatus _status = OrderBookStatus.Closed;
        private long _nextSequenceNumber;
        private long? _lastTradedPrice;

        // Anchor for price banding, kept separate from _lastTradedPrice: it's seeded from an
        // explicit reference price (mirroring CME's settlement price pre-open) before any trade
        // has happened, whereas _lastTradedPrice being null specifically means "no trade yet" for
        // the stop-trigger reasonability checks elsewhere. Tracks _lastTradedPrice once trading
        // starts, so the band dynamically slides with the market like CME's does.
        private long? _bandReferencePriceTicks;

        // Array-backed, indexed by tick count (price / Security.TickSize) rather than decimal —
        // see InternalOrder and PriceLadder for why.
        private readonly Dictionary<Side, PriceLadder> _working = new()
        {
            {Side.Buy, new PriceLadder(descending: true)},
            {Side.Sell, new PriceLadder(descending: false)}
        };

        private readonly Dictionary<Side, PriceLadder> _stops = new()
        {
            {Side.Buy, new PriceLadder(descending: false)},
            {Side.Sell, new PriceLadder(descending: true)}
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

        public IReadOnlyList<Level> GetLevels(Side side, int maxPrices)
        {
            return _working[side].EnumerateFromBest().Take(maxPrices)
                .Select(x => new Level(ToDecimal(x.Tick), SumRemaining(x.First), x.Count))
                .ToList();
        }

        private static int SumRemaining(InternalOrder? first)
        {
            var total = 0;
            for (var order = first; order != null; order = order.LevelNext)
                total += order.RemainingQuantity;
            return total;
        }

        public IReadOnlyList<OrderBookEvent> Process(OrderBookAction action)
        {
            return action switch
            {
                CreateLimitOrder o => CreateOrder(o.CompanyId, o.ClientOrderId, o.OrderValidity, o.Side, o.Quantity,
                    OrderType.Limit, o.Price, null, o.SelfMatchPrevention),
                CreateMarketOrder o => CreateOrder(o.CompanyId, o.ClientOrderId, o.OrderValidity, o.Side, o.Quantity,
                    OrderType.Market, null, null, o.SelfMatchPrevention),
                CreateMarketLimitOrder o => CreateOrder(o.CompanyId, o.ClientOrderId, o.OrderValidity, o.Side,
                    o.Quantity, OrderType.MarketLimit, null, null, o.SelfMatchPrevention),
                CreateStopLimitOrder o => CreateOrder(o.CompanyId, o.ClientOrderId, o.OrderValidity, o.Side,
                    o.Quantity, OrderType.StopLimit, o.Price, o.TriggerPrice, o.SelfMatchPrevention),
                CreateStopMarketOrder o => CreateOrder(o.CompanyId, o.ClientOrderId, o.OrderValidity, o.Side,
                    o.Quantity, OrderType.StopMarket, null, o.TriggerPrice, o.SelfMatchPrevention),
                UpdateOrder update => UpdateOrder(update.CompanyId, update.ClientOrderId,
                    update.PreviousClientOrderId, update.NewTotalQuantity, update.Price, update.TriggerPrice),
                CancelOrder cancel => CancelOrder(cancel.CompanyId, cancel.ClientOrderId, cancel.PreviousClientOrderId),
                PreOpenTrading s => UpdateStatus(OrderBookStatus.PreOpen, s.ReferencePrice),
                OpenTrading s => UpdateStatus(OrderBookStatus.Open, s.ReferencePrice),
                CloseTrading => UpdateStatus(OrderBookStatus.Closed, null),
                _ => throw new ArgumentException("Unknown order book action")
            };
        }

        private List<OrderBookEvent> CreateOrder(string companyId, string clientOrderId, OrderValidity validity,
            Side side, int quantity, OrderType type, decimal? price = null, decimal? triggerPrice = null,
            SelfMatchPrevention? selfMatchPrevention = null)
        {
            var selfMatchPreventionId = selfMatchPrevention?.Id;
            var selfMatchPreventionInstruction = selfMatchPrevention?.Instruction;
            var status = triggerPrice.HasValue ? OrderStatus.Hidden : OrderStatus.Working;

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
            if (priceTicks.HasValue && !IsWithinPriceBand(priceTicks.Value))
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.PriceOutsideBands);
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

            if (validity is OrderValidity.FillOrKill && !triggerTicks.HasValue &&
                !HasSufficientLiquidity(side, priceTicks!.Value, quantity, selfMatchPreventionId,
                    selfMatchPreventionInstruction))
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.InsufficientLiquidityForFillOrKill);
            if (validity is OrderValidity.GoodTilDate { Date: var goodTilDate } && goodTilDate < DateOnly.FromDateTime(Now()))
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.InvalidExpireDate);

            _nextSequenceNumber++;
            var order = new InternalOrder(_nextSequenceNumber, companyId, clientOrderId, _security, Now(), status,
                type, validity, side, quantity, priceTicks, triggerTicks, selfMatchPreventionId,
                selfMatchPreventionInstruction);

            _orders.Add(order.ExchangeOrderId, order);
            _clientOrderIndex.Add((companyId, clientOrderId), order);
            var orders = (triggerTicks.HasValue ? _stops : _working);
            var newPriceTicks = (triggerTicks ?? priceTicks) ?? throw new Exception("error");
            orders[side].Add(newPriceTicks, order);

            List<OrderBookEvent> events = new();
            events.Add(new CreateOrderConfirmed(_security, Now(), companyId, order.ToOrder()));
            Match(events);

            if (order.Validity is OrderValidity.FillAndKill && order.Status == OrderStatus.Working)
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
            if (!opposing.TryGetBest(out var bestTick, out _))
                return false;

            // set price as best offer + protection ticks for buy orders, best bid - protection ticks for sell orders
            // TODO: option to use best bid + protection tickets for buy orders, etc (eurex)
            priceTicks = bestTick + ((side == Side.Buy ? 1 : -1) * protectionTicks);
            return true;
        }

        // Only client-supplied resting limit prices go through this - not trigger prices (already
        // governed by the TriggerPriceMustBe.../LastTradedPrice checks above) and not the computed
        // effective price for Market/MarketLimit orders (already governed by the separate
        // MarketOrderProtectionTicks mechanism). Band is inactive (returns true) until both a band
        // width is configured and a reference price has been established.
        private bool IsWithinPriceBand(long priceTicks) =>
            !_security.PriceBandTicks.HasValue || !_bandReferencePriceTicks.HasValue ||
            Math.Abs(priceTicks - _bandReferencePriceTicks.Value) <= _security.PriceBandTicks.Value;

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
            foreach (var (tick, first, _) in opposing.EnumerateFromBest())
            {
                var crosses = side == Side.Buy ? tick <= priceTicks : tick >= priceTicks;
                if (!crosses)
                    break;

                for (var restingOrder = first; restingOrder != null; restingOrder = restingOrder.LevelNext)
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

        private List<OrderBookEvent> UpdateOrder(string companyId, string clientOrderId, string previousClientOrderId,
            int? newTotalQuantity = null, decimal? price = null, decimal? triggerPrice = null)
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
            if (newTotalQuantity == null && price == null && triggerPrice == null)
                return RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.NoChange);
            if (newTotalQuantity != null && newTotalQuantity < 1)
                return RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.InvalidQuantity);
            if (!TryConvertToTicks(price, out var priceTicks))
                return RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.InvalidPriceIncrement);
            if (!TryConvertToTicks(triggerPrice, out var triggerTicks))
                return RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.InvalidPriceIncrement);
            if (priceTicks.HasValue && !IsWithinPriceBand(priceTicks.Value))
                return RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.PriceOutsideBands);
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

            if (newTotalQuantity <= order.FilledQuantity)
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
            var isQuantityIncrease = (newTotalQuantity != null && newTotalQuantity > order.Quantity);

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
                orders[order.Side].Remove(currentPriceTicks, order);
                orders[order.Side].Add(updatedPriceTicks, order);
            }
            order.Update(sequenceNumber, Now(), newTotalQuantity, triggerTicks, priceTicks, clientOrderId);
            _clientOrderIndex[(companyId, clientOrderId)] = order;

            List<OrderBookEvent> events = new();
            events.Add(new UpdateOrderConfirmed(_security, Now(), order.CompanyId, order.ToOrder(), previousClientOrderId));
            Match(events);
            return events;
        }

        private List<OrderBookEvent> CancelOrder(string companyId, string clientOrderId, string previousClientOrderId)
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
                _stops[order.Side].Remove(price, order);
            }
            else
            {
                var price = order.Price ?? throw new InvalidOperationException("limit order missing price");
                _working[order.Side].Remove(price, order);
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

        private InternalOrder? BestOrder(Side side) =>
            _working[side].TryGetBest(out _, out var order) ? order : null;

        private void Match(List<OrderBookEvent> events)
        {
            if (_status != OrderBookStatus.Open)
            {
                return;
            }

            var time = Now();

            var buy = BestOrder(Side.Buy);
            var sell = BestOrder(Side.Sell);

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

                    buy = BestOrder(Side.Buy);
                    sell = BestOrder(Side.Sell);
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
                    _bandReferencePriceTicks = priceTicks;
                    CheckStops(events);
                }

                buy = BestOrder(Side.Buy);
                sell = BestOrder(Side.Sell);
            }
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

        private void CheckStops(List<OrderBookEvent> events)
        {
            var time = Now();
            var triggered = new SortedDictionary<long, InternalOrder>();

            while (_stops[Side.Buy].TryGetBest(out var buyTick, out var buyFirst) && buyTick <= _lastTradedPrice)
            {
                for (var order = buyFirst; order != null; order = order.LevelNext)
                    triggered.Add(order.SequenceNumber, order);
                _stops[Side.Buy].RemoveLevel(buyTick);
            }

            while (_stops[Side.Sell].TryGetBest(out var sellTick, out var sellFirst) && sellTick >= _lastTradedPrice)
            {
                for (var order = sellFirst; order != null; order = order.LevelNext)
                    triggered.Add(order.SequenceNumber, order);
                _stops[Side.Sell].RemoveLevel(sellTick);
            }

            if (triggered.Any())
            {
                TriggerStops(triggered, time, events);
                Match(events);

                foreach (var order in triggered.Values)
                {
                    if (order.Validity is OrderValidity.FillAndKill && order.RemainingQuantity > 0)
                        events.Add(CancelRemainder(order, OrderCancelledReason.FillAndKillNotFilled));
                }
            }
        }

        private void TriggerStops(SortedDictionary<long, InternalOrder> orders, DateTime time,
            List<OrderBookEvent> events)
        {
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

                if (order.Validity is OrderValidity.FillOrKill &&
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
                _working[order.Side].Add(limitPriceTicks, order);

                events.Add(new UpdateOrderConfirmed(_security, time, order.CompanyId, order.ToOrder(),
                    order.ClientOrderId));
            }
        }

        private List<OrderBookEvent> UpdateStatus(OrderBookStatus status, decimal? referencePrice = null)
        {
            if (referencePrice.HasValue && TryConvertToTicks(referencePrice, out var referenceTicks))
                _bandReferencePriceTicks = referenceTicks;

            return status switch
            {
                OrderBookStatus.PreOpen => PreOpenMarket(),
                OrderBookStatus.Open => OpenMarket(),
                OrderBookStatus.Closed => CloseMarket(),
                _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
            };
        }

        private List<OrderBookEvent> PreOpenMarket()
        {
            // TODO: need better system for multiple sessions per day
            var date = Now();
            _nextSequenceNumber = ((date.Year * 10000) + (date.Month * 100) + date.Day) * 10000000000L;
            _status = OrderBookStatus.PreOpen;
            return new List<OrderBookEvent> {new StatusChanged(_security, Now(), _status)};
        }

        private List<OrderBookEvent> OpenMarket()
        {
            _status = OrderBookStatus.Open;
            var events = new List<OrderBookEvent> {new StatusChanged(_security, Now(), _status)};
            Match(events);
            return events;
        }

        private List<OrderBookEvent> CloseMarket()
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
                o.Validity is OrderValidity.Day ||
                (o.Validity is OrderValidity.GoodTilDate { Date: var date } && date <= today)).ToList();

            return orders.Select(ExpireOrder).ToList();
        }
    }

    public record Level(decimal Price, int Quantity, int Count);
}
