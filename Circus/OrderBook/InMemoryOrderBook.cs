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
        private OrderBookStatus _status = OrderBookStatus.Closed;

        private readonly ITimeProvider _timeProvider;

        private long _nextSequenceNumber;

        private readonly SortedDictionary<decimal, SortedDictionary<long, InternalOrder>> _buyOrders =
            new(new DescendingComparer());

        private readonly SortedDictionary<decimal, SortedDictionary<long, InternalOrder>> _sellOrders = new();
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
            var orders = side == Side.Buy ? _buyOrders : _sellOrders;
            return orders.Take(maxPrices)
                .Select(x => new Level(
                    x.Key,
                    x.Value.Sum(y => y.Value.RemainingQuantity),
                    x.Value.Count))
                .ToList();
        }

        public IList<OrderBookEvent> Process(OrderBookAction action)
        {
            return action switch
            {
                CreateLimitOrder create => CreateLimitOrder(create.ClientId, create.OrderId, create.OrderValidity,
                    create.Side, create.Price, create.Quantity),
                CreateMarketOrder create => CreateMarketOrder(create.ClientId, create.OrderId, create.OrderValidity,
                    create.Side, create.Quantity),
                UpdateLimitOrder update => UpdateLimitOrder(update.ClientId, update.OrderId, update.Price,
                    update.Quantity),
                CancelOrder cancel => CancelOrder(cancel.ClientId, cancel.OrderId),
                UpdateStatus update => UpdateStatus(update.Status),
                _ => throw new ArgumentException("Unknown order book action")
            };
        }


        public IList<OrderBookEvent> CreateLimitOrder(Guid clientId, Guid orderId, OrderValidity validity, Side side,
            decimal price, int quantity)
        {
            if (_status == OrderBookStatus.Closed)
                return RejectCreate(clientId, orderId, OrderRejectedReason.MarketClosed);
            if (price % _security.TickSize != 0)
                return RejectCreate(clientId, orderId, OrderRejectedReason.InvalidPriceIncrement);
            if (quantity < 1)
                return RejectCreate(clientId, orderId, OrderRejectedReason.InvalidQuantity);

            _nextSequenceNumber++;
            var order = new InternalOrder(_nextSequenceNumber, clientId, orderId, _security, Now(), OrderType.Limit,
                validity, side, price,
                quantity);
            _orders.Add(order.OrderId, order);

            var orders = order.Side == Side.Buy ? _buyOrders : _sellOrders;
            orders.Add(order);

            Console.WriteLine($"order added: {order}");

            List<OrderBookEvent> events = new();
            events.Add(new CreateOrderConfirmed(_security, Now(), clientId, order.ToOrder()));

            if (_status == OrderBookStatus.Open)
            {
                events.AddRange(Match());
            }

            return events;
        }

        public IList<OrderBookEvent> CreateMarketOrder(Guid clientId, Guid orderId, OrderValidity validity, Side side,
            int quantity)
        {
            if (_status == OrderBookStatus.Closed)
                return RejectCreate(clientId, orderId, OrderRejectedReason.MarketClosed);
            if (_status == OrderBookStatus.PreOpen)
                return RejectCreate(clientId, orderId, OrderRejectedReason.MarketPreOpen);
            if (quantity < 1)
                return RejectCreate(clientId, orderId, OrderRejectedReason.InvalidQuantity);

            var oppositeOrders = side == Side.Buy ? _sellOrders : _buyOrders;
            if (!oppositeOrders.Any())
                return RejectCreate(clientId, orderId, OrderRejectedReason.NoOrdersToMatchMarketOrder);

            // set price as best offer + protection ticks for buy orders, best bid - protection ticks for sell orders
            // TODO: option to use best bid + protection tickets for buy orders, etc (eurex)
            var price = oppositeOrders.First().Key +
                        ((side == Side.Buy ? 1 : -1) * (_security.MarketOrderProtectionTicks * _security.TickSize));

            _nextSequenceNumber++;
            var order = new InternalOrder(_nextSequenceNumber, clientId, orderId, _security, Now(), OrderType.Market,
                validity, side, price,
                quantity);
            _orders.Add(order.OrderId, order);

            var orders = order.Side == Side.Buy ? _buyOrders : _sellOrders;
            orders.Add(order);

            Console.WriteLine($"order added: {order}");

            List<OrderBookEvent> events = new();
            events.Add(new CreateOrderConfirmed(_security, Now(), order.ClientId, order.ToOrder()));
            events.AddRange(Match());

            if (order.Status == OrderStatus.Working)
            {
                order.ConvertToLimit();
            }

            return events;
        }

        public IList<OrderBookEvent> UpdateLimitOrder(Guid clientId, Guid orderId, decimal price, int quantity)
        {
            if (_status == OrderBookStatus.Closed)
                return RejectUpdate(clientId, orderId, OrderRejectedReason.MarketClosed);
            if (price % _security.TickSize != 0)
                return RejectUpdate(clientId, orderId, OrderRejectedReason.InvalidPriceIncrement);
            if (quantity < 1)
                return RejectUpdate(clientId, orderId, OrderRejectedReason.InvalidQuantity);
            if (_completedOrders.ContainsKey(orderId))
                return RejectUpdate(clientId, orderId, OrderRejectedReason.TooLateToCancel);
            if (!_orders.ContainsKey(orderId))
                return RejectUpdate(clientId, orderId, OrderRejectedReason.OrderNotInBook);
            var order = _orders[orderId];

            if (quantity <= order.FilledQuantity)
            {
                order.Cancel(Now());
                CompleteOrder(order);

                Console.WriteLine($"order cancelled on update as new quantity <= filled quantity: {order}");

                return new List<OrderBookEvent>()
                {
                    new CancelOrderConfirmed(_security, Now(), order.ClientId, order.ToOrder(),
                        OrderCancelledReason.UpdatedQuantityLowerThanFilledQuantity)
                };
            }

            var isPriceChange = price != order.Price;
            var isQuantityIncrease = quantity > order.Quantity;

            var orders = order.Side == Side.Buy ? _buyOrders : _sellOrders;
            var sequenceNumber = order.SequenceNumber;
            if (isPriceChange || isQuantityIncrease)
            {
                orders.Remove(order);
                _nextSequenceNumber++;
                sequenceNumber = _nextSequenceNumber;
            }

            order.Update(sequenceNumber, Now(), price, quantity);
            if (isPriceChange || isQuantityIncrease)
            {
                orders.Add(order);
            }

            Console.WriteLine($"order updated: {order}");

            List<OrderBookEvent> events = new();
            events.Add(new UpdateOrderConfirmed(_security, Now(), order.ClientId, order.ToOrder()));

            if (_status == OrderBookStatus.Open && isPriceChange)
            {
                events.AddRange(Match());
            }

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

            Console.WriteLine($"order cancelled: {order}");

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

            Console.WriteLine($"order expired: {order}");

            return new ExpireOrderConfirmed(_security, Now(), order.ClientId, order.ToOrder());
        }

        private void CompleteOrder(InternalOrder order)
        {
            var orders = order.Side == Side.Buy ? _buyOrders : _sellOrders;
            orders.Remove(order);
            _orders.Remove(order.OrderId);
            _completedOrders.Add(order.OrderId, order);
        }

        private IEnumerable<OrderBookEvent> Match()
        {
            var time = Now();

            var buy = _buyOrders.FirstOrDefault().Value?.FirstOrDefault().Value;
            var sell = _sellOrders.FirstOrDefault().Value?.FirstOrDefault().Value;

            var events = new List<OrderBookEvent>();

            while (buy != null && sell != null && buy.Price >= sell.Price)
            {
                var resting = buy.ModifiedTime < sell.ModifiedTime ? buy : sell;
                var aggressor = buy == resting ? sell : buy;

                var quantity = Math.Min(resting.RemainingQuantity, aggressor.RemainingQuantity);
                var price = resting.Price;

                Console.WriteLine($"matched orders: {quantity}@{price}");
                Console.WriteLine($"- resting   {resting}");
                Console.WriteLine($"- aggressor {aggressor}");

                FillOrder(resting, time, quantity);
                FillOrder(aggressor, time, quantity);

                events.Add(new OrdersMatched(
                    _security, time, price, quantity,
                    new[]
                    {
                        new FillOrderConfirmed(_security, time, resting.ClientId, resting.ToOrder(), price, quantity,
                            true),
                        new FillOrderConfirmed(_security, time, aggressor.ClientId, aggressor.ToOrder(), price,
                            quantity, false)
                    }
                ));

                buy = _buyOrders.FirstOrDefault().Value?.FirstOrDefault().Value;
                sell = _sellOrders.FirstOrDefault().Value?.FirstOrDefault().Value;
            }

            return events;
        }

        private void FillOrder(InternalOrder order, DateTime time, int quantity)
        {
            order.Fill(time, quantity);
            if (order.Status == OrderStatus.Filled)
            {
                CompleteOrder(order);
            }
        }

        // private void CheckStops(decimal maxTradePrice, decimal minTradePrice)
        // {
        //     // check stops
        //     var triggered = false;
        //     foreach (var order in StopOrders())
        //     {
        //         if ((order.Side == Side.Buy && maxTradePrice < order.StopPrice) ||
        //             (order.Side == Side.Sell && minTradePrice > order.StopPrice))
        //         {
        //             continue;
        //         }
        //
        //         Console.WriteLine($"stop order triggered: {order.Id}");
        //         // order.Status = OrderStatus.Created;
        //         // order.ModifiedTime = DateTime.Now;
        //
        //         // set protected limit
        //         if (order.Type == OrderType.Stop)
        //         {
        //             var top = WorkingOrders(order.Side == Side.Buy ? Side.Sell : Side.Buy).FirstOrDefault();
        //
        //             // TODO: what to do if book is empty?
        //             if (top == null)
        //             {
        //                 throw new NotImplementedException(
        //                     "no orders in market after stop triggered, unable to set a protected price");
        //             }
        //
        //             var protectionPoints = 10 * (order.Side == Side.Buy ? 1 : -1);
        //
        //             // order.Price = top.Price + protectionPoints;
        //         }
        //
        //         triggered = true;
        //     }
        //
        //     if (triggered)
        //     {
        //         Match();
        //     }
        // }

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
            events.AddRange(ExpireDayOrders());
            return events;
        }

        private IEnumerable<OrderBookEvent> ExpireDayOrders()
        {
            var orders = _orders.Values.Where(o => o.Validity == OrderValidity.Day).ToList();

            return orders.Select(ExpireOrder).ToList();
        }
    }

    public record Level(decimal Price, int Quantity, int Count);

    internal static class SortedDictionaryExtensions
    {
        internal static void Add(this SortedDictionary<decimal, SortedDictionary<long, InternalOrder>> orders,
            InternalOrder order)
        {
            if (orders.ContainsKey(order.Price))
            {
                orders[order.Price].Add(order.SequenceNumber, order);
            }
            else
            {
                orders[order.Price] = new SortedDictionary<long, InternalOrder> {{order.SequenceNumber, order}};
            }
        }

        internal static void Remove(this SortedDictionary<decimal, SortedDictionary<long, InternalOrder>> orders,
            InternalOrder order)
        {
            orders[order.Price].Remove(order.SequenceNumber);

            if (orders[order.Price].Count == 0)
            {
                orders.Remove(order.Price);
            }
        }
    }
}