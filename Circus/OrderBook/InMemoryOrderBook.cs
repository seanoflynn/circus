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

        private readonly Dictionary<Guid, InternalOrder> _orders = new();
        private readonly Dictionary<Guid, InternalOrder> _completedOrders = new();

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
                CreateOrder create => CreateOrder(create.ClientId, create.OrderId, create.OrderValidity, create.Side,
                    create.Quantity, create.Price, create.TriggerPrice, create.MarketLimit, create.GoodTilDate),
                UpdateOrder update => UpdateOrder(update.ClientId, update.OrderId, update.Quantity, update.Price,
                    update.TriggerPrice),
                CancelOrder cancel => CancelOrder(cancel.ClientId, cancel.OrderId),
                UpdateStatus update => UpdateStatus(update.Status),
                _ => throw new ArgumentException("Unknown order book action")
            };
        }

        public IList<OrderBookEvent> CreateOrder(Guid clientId, Guid orderId, OrderValidity validity, Side side,
            int quantity, decimal? price = null, decimal? triggerPrice = null, bool marketLimit = false,
            DateOnly? goodTilDate = null)
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
                return RejectCreate(clientId, orderId, OrderRejectedReason.MarketClosed);
            if (type == OrderType.Market && _status == OrderBookStatus.PreOpen)
                return RejectCreate(clientId, orderId, OrderRejectedReason.MarketPreOpen);
            if (quantity < 1)
                return RejectCreate(clientId, orderId, OrderRejectedReason.InvalidQuantity);
            if (!TryConvertToTicks(price, out var priceTicks))
                return RejectCreate(clientId, orderId, OrderRejectedReason.InvalidPriceIncrement);
            if (!TryConvertToTicks(triggerPrice, out var triggerTicks))
                return RejectCreate(clientId, orderId, OrderRejectedReason.InvalidPriceIncrement);
            if (triggerTicks != null && priceTicks != null && side == Side.Buy && priceTicks < triggerTicks)
                return RejectCreate(clientId, orderId, OrderRejectedReason.TriggerPriceMustBeLessThanPrice);
            if (triggerTicks != null && priceTicks != null && side == Side.Sell && priceTicks > triggerTicks)
                return RejectCreate(clientId, orderId, OrderRejectedReason.TriggerPriceMustBeGreaterThanPrice);
            if (triggerTicks != null && !_lastTradedPrice.HasValue)
                return RejectCreate(clientId, orderId, OrderRejectedReason.NoLastTradedPrice);
            if (triggerTicks != null && side == Side.Buy && triggerTicks <= _lastTradedPrice)
                return RejectCreate(clientId, orderId, OrderRejectedReason.TriggerPriceMustBeGreaterThanLastTradedPrice);
            if (triggerTicks != null && side == Side.Sell && triggerTicks >= _lastTradedPrice)
                return RejectCreate(clientId, orderId, OrderRejectedReason.TriggerPriceMustBeLessThanLastTradedPrice);
            if (_orders.ContainsKey(orderId))
                return RejectCreate(clientId, orderId, OrderRejectedReason.OrderInBook);
            if (_completedOrders.ContainsKey(orderId))
                return RejectCreate(clientId, orderId, OrderRejectedReason.OrderIdAlreadyUsed);

            if (type == OrderType.Market || type == OrderType.MarketLimit)
            {
                var protectionTicks = type == OrderType.MarketLimit ? 0 : _security.MarketOrderProtectionTicks;
                if(!TryGetLimitPrice(side, protectionTicks, out priceTicks))
                    return RejectCreate(clientId, orderId, OrderRejectedReason.NoOrdersToMatchMarketOrder);
            }

            if (validity == OrderValidity.FillOrKill && !triggerTicks.HasValue &&
                !HasSufficientLiquidity(side, priceTicks!.Value, quantity))
                return RejectCreate(clientId, orderId, OrderRejectedReason.InsufficientLiquidityForFillOrKill);
            if (validity == OrderValidity.GoodTilDate && !goodTilDate.HasValue)
                return RejectCreate(clientId, orderId, OrderRejectedReason.GoodTilDateRequired);
            if (goodTilDate.HasValue && goodTilDate.Value < DateOnly.FromDateTime(Now()))
                return RejectCreate(clientId, orderId, OrderRejectedReason.InvalidExpireDate);

            _nextSequenceNumber++;
            var order = new InternalOrder(_nextSequenceNumber, clientId, orderId, _security, Now(), status, type,
                validity, side, quantity, priceTicks, triggerTicks, goodTilDate);

            _orders.Add(orderId, order);
            var orders = (triggerTicks.HasValue ? _stops : _working);
            var newPriceTicks = (triggerTicks ?? priceTicks) ?? throw new Exception("error");
            orders[side].Add(newPriceTicks, _nextSequenceNumber, order);

            List<OrderBookEvent> events = new();
            events.Add(new CreateOrderConfirmed(_security, Now(), clientId, order.ToOrder()));
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

        private bool HasSufficientLiquidity(Side side, long priceTicks, int quantity)
        {
            var opposing = _working[side == Side.Buy ? Side.Sell : Side.Buy];
            var total = 0;
            foreach (var level in opposing)
            {
                var crosses = side == Side.Buy ? level.Key <= priceTicks : level.Key >= priceTicks;
                if (!crosses)
                    break;

                total += level.Value.Sum(o => o.Value.RemainingQuantity);
                if (total >= quantity)
                    return true;
            }

            return total >= quantity;
        }

        private OrderBookEvent CancelRemainder(InternalOrder order, OrderCancelledReason reason)
        {
            order.Cancel(Now());
            CompleteOrder(order);
            return new CancelOrderConfirmed(_security, Now(), order.ClientId, order.ToOrder(), reason);
        }

        public IList<OrderBookEvent> UpdateOrder(Guid clientId, Guid orderId, int? quantity = null, decimal? price = null,
            decimal? triggerPrice = null)
        {
            if (_status == OrderBookStatus.Closed)
                return RejectUpdate(clientId, orderId, OrderRejectedReason.MarketClosed);
            if (quantity == null && price == null && triggerPrice == null)
                return RejectUpdate(clientId, orderId, OrderRejectedReason.NoChange);
            if (quantity != null && quantity < 1)
                return RejectUpdate(clientId, orderId, OrderRejectedReason.InvalidQuantity);
            if (!TryConvertToTicks(price, out var priceTicks))
                return RejectUpdate(clientId, orderId, OrderRejectedReason.InvalidPriceIncrement);
            if (!TryConvertToTicks(triggerPrice, out var triggerTicks))
                return RejectUpdate(clientId, orderId, OrderRejectedReason.InvalidPriceIncrement);
            if (_completedOrders.ContainsKey(orderId))
                return RejectUpdate(clientId, orderId, OrderRejectedReason.TooLateToCancel);
            if (!_orders.ContainsKey(orderId))
                return RejectUpdate(clientId, orderId, OrderRejectedReason.OrderNotInBook);

            var order = _orders[orderId];

            if (order.Status == OrderStatus.Hidden)
            {
                var newTriggerTicks = triggerTicks ?? order.TriggerPrice;
                var newPriceTicks = priceTicks ?? order.Price;

                if (newTriggerTicks != null && newPriceTicks != null && order.Side == Side.Buy && newPriceTicks < newTriggerTicks)
                    return RejectUpdate(clientId, orderId, OrderRejectedReason.TriggerPriceMustBeLessThanPrice);
                if (newTriggerTicks != null && newPriceTicks != null && order.Side == Side.Sell && newPriceTicks > newTriggerTicks)
                    return RejectUpdate(clientId, orderId, OrderRejectedReason.TriggerPriceMustBeGreaterThanPrice);

                if (triggerTicks != null && order.Side == Side.Buy && triggerTicks <= _lastTradedPrice)
                    return RejectUpdate(clientId, orderId,
                        OrderRejectedReason.TriggerPriceMustBeGreaterThanLastTradedPrice);
                if (triggerTicks != null && order.Side == Side.Sell && triggerTicks >= _lastTradedPrice)
                    return RejectUpdate(clientId, orderId,
                        OrderRejectedReason.TriggerPriceMustBeLessThanLastTradedPrice);
            }
            else
            {
                // ignore trigger price if already triggered
                triggerTicks = null;
            }

            // TODO: can't update price on stop market order?

            if (quantity <= order.FilledQuantity)
            {
                order.Cancel(Now());
                CompleteOrder(order);

                return new List<OrderBookEvent>
                {
                    new CancelOrderConfirmed(_security, Now(), order.ClientId, order.ToOrder(),
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
            order.Update(sequenceNumber, Now(), quantity, triggerTicks, priceTicks);

            List<OrderBookEvent> events = new();
            events.Add(new UpdateOrderConfirmed(_security, Now(), order.ClientId, order.ToOrder()));
            events.AddRange(Match());
            return events;
        }

        public IList<OrderBookEvent> CancelOrder(Guid clientId, Guid orderId)
        {
            if (_status == OrderBookStatus.Closed)
                return RejectCancel(clientId, orderId, OrderRejectedReason.MarketClosed);
            if (_completedOrders.ContainsKey(orderId))
                return RejectCancel(clientId, orderId, OrderRejectedReason.TooLateToCancel);
            if (!_orders.ContainsKey(orderId))
                return RejectCancel(clientId, orderId, OrderRejectedReason.OrderNotInBook);
            var order = _orders[orderId];

            order.Cancel(Now());
            CompleteOrder(order);

            return new List<OrderBookEvent>
            {
                new CancelOrderConfirmed(_security, Now(), order.ClientId, order.ToOrder(),
                    OrderCancelledReason.Cancelled)
            };
        }

        private List<OrderBookEvent> RejectCreate(Guid clientId, Guid orderId, OrderRejectedReason reason) =>
            new() {new CreateOrderRejected(_security, Now(), clientId, orderId, reason)};

        private List<OrderBookEvent> RejectUpdate(Guid clientId, Guid orderId, OrderRejectedReason reason) =>
            new() {new UpdateOrderRejected(_security, Now(), clientId, orderId, reason)};

        private List<OrderBookEvent> RejectCancel(Guid clientId, Guid orderId, OrderRejectedReason reason) =>
            new() {new CancelOrderRejected(_security, Now(), clientId, orderId, reason)};

        private OrderBookEvent ExpireOrder(InternalOrder order)
        {
            order.Expire(Now());
            CompleteOrder(order);


            return new ExpireOrderConfirmed(_security, Now(), order.ClientId, order.ToOrder());
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
            _orders.Remove(order.OrderId);
            _completedOrders.Add(order.OrderId, order);
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

                var quantity = Math.Min(resting.RemainingQuantity, aggressor.RemainingQuantity);
                var priceTicks = resting.Price ?? throw new InvalidOperationException("limit order requires price");
                var price = ToDecimal(priceTicks);


                FillOrder(resting, time, quantity);
                FillOrder(aggressor, time, quantity);

                events.Add(new OrdersMatched(_security, time, price, quantity,
                    new[]
                    {
                        new FillOrderConfirmed(_security, time, resting.ClientId, resting.ToOrder(), price, quantity,
                            true),
                        new FillOrderConfirmed(_security, time, aggressor.ClientId, aggressor.ToOrder(), price,
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
                    order.Cancel(Now());
                    FinishOrder(order);

                    events.Add(new CancelOrderConfirmed(_security, Now(), order.ClientId, order.ToOrder(),
                        OrderCancelledReason.NoOrdersToMatchMarketOrder));
                    continue;
                }

                if (order.Validity == OrderValidity.FillOrKill &&
                    !HasSufficientLiquidity(order.Side, newPriceTicks!.Value, order.RemainingQuantity))
                {
                    order.Cancel(Now());
                    FinishOrder(order);

                    events.Add(new CancelOrderConfirmed(_security, Now(), order.ClientId, order.ToOrder(),
                        OrderCancelledReason.FillOrKillNotFilled));
                    continue;
                }

                _nextSequenceNumber++;
                order.ConvertToLimit(time, _nextSequenceNumber, newPriceTicks);

                var limitPriceTicks = order.Price ?? throw new Exception("missing price");
                _working[order.Side].Add(limitPriceTicks, order.SequenceNumber, order);

                events.Add(new UpdateOrderConfirmed(_security, time, order.ClientId, order.ToOrder()));
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
