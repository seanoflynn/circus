using System;
using System.Collections.Generic;
using System.Linq;
using Circus.TimeProviders;
using Circus.Util;

namespace Circus.OrderBook
{
    public class InMemoryOrderBook : IOrderBook
    {
        public event EventHandler<OrderBookEventArgs>? OrderBookEvent;

        public void Process(OrderBookAction action)
        {
            switch (action)
            {
                case CreateLimitOrder create:
                    CreateLimitOrder(create.ClientId, create.OrderId, create.OrderValidity, create.Side, create.Price, create.Quantity);
                    break;
                case CreateMarketOrder create:
                    CreateMarketOrder(create.ClientId, create.OrderId, create.OrderValidity, create.Side, create.Quantity);
                    break;
                case UpdateLimitOrder update:
                    UpdateLimitOrder(update.ClientId, update.OrderId, update.Price, update.Quantity);
                    break;
                case CancelOrder cancel:
                    CancelOrder(cancel.ClientId, cancel.OrderId);
                    break;
                case UpdateStatus update:
                    UpdateStatus(update.Status);
                    break;
                default:
                    throw new ArgumentException("Unknown order book action");
            }
        }

        public Security Security { get; }
        public OrderBookStatus Status { get; private set; } = OrderBookStatus.Closed;

        private readonly ITimeProvider _timeProvider;
        private DateTime Now() => _timeProvider.GetCurrentTime();

        private long _nextSequenceNumber;

        private readonly SortedDictionary<decimal, SortedDictionary<long, InternalOrder>> _buyOrders =
            new(new DescendingComparer());

        private readonly SortedDictionary<decimal, SortedDictionary<long, InternalOrder>> _sellOrders = new();
        private readonly Dictionary<Guid, InternalOrder> _orders = new();
        private readonly Dictionary<Guid, InternalOrder> _completedOrders = new();

        public InMemoryOrderBook(Security security, ITimeProvider timeProvider)
        {
            Security = security;
            _timeProvider = timeProvider;
        }

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

        public void CreateLimitOrder(Guid clientId, Guid orderId, OrderValidity validity, Side side, decimal price,
            int quantity)
        {
            if (ValidateCreate(clientId, orderId, OrderRejectedReason.MarketClosed, 
                () => Status == OrderBookStatus.Closed)) return;
            if (ValidateCreate(clientId, orderId, OrderRejectedReason.InvalidPriceIncrement,
                () => price % Security.TickSize != 0)) return;
            if (ValidateCreate(clientId, orderId, OrderRejectedReason.InvalidQuantity, 
                () => quantity < 1)) return;

            _nextSequenceNumber++;
            var order = new InternalOrder(_nextSequenceNumber, clientId, orderId, Security, Now(), OrderType.Limit, validity, side, price,
                quantity);
            _orders.Add(order.OrderId, order);

            var orders = order.Side == Side.Buy ? _buyOrders : _sellOrders;
            orders.Add(order);

            Console.WriteLine($"order added: {order}");

            var events = new List<OrderBookEvent> {new CreateOrderConfirmed(Security, Now(), clientId, order.ToOrder())};

            if (Status == OrderBookStatus.Open)
            {
                events.AddRange(Match());
            }

            FireEvents(events.ToArray());
        }

        public void CreateMarketOrder(Guid clientId, Guid orderId, OrderValidity validity, Side side, int quantity)
        {
            if (ValidateCreate(clientId, orderId, OrderRejectedReason.MarketClosed, 
                () => Status == OrderBookStatus.Closed)) return;
            if (ValidateCreate(clientId, orderId, OrderRejectedReason.MarketPreOpen, 
                () => Status == OrderBookStatus.PreOpen)) return;
            if (ValidateCreate(clientId, orderId, OrderRejectedReason.InvalidQuantity, 
                () => quantity < 1)) return;

            var oppositeOrders = side == Side.Buy ? _sellOrders : _buyOrders;
            if (ValidateCreate(clientId, orderId, OrderRejectedReason.NoOrdersToMatchMarketOrder, 
                () => !oppositeOrders.Any())) return;

            // set price as best offer + protection ticks for buy orders, best bid - protection ticks for sell orders
            // TODO: option to use best bid + protection tickets for buy orders, etc (eurex)
            var price = oppositeOrders.First().Key +
                        ((side == Side.Buy ? 1 : -1) * (Security.MarketOrderProtectionTicks * Security.TickSize));

            _nextSequenceNumber++;
            var order = new InternalOrder(_nextSequenceNumber, clientId, orderId, Security, Now(), OrderType.Market, validity, side, price,
                quantity);
            _orders.Add(order.OrderId, order);

            var orders = order.Side == Side.Buy ? _buyOrders : _sellOrders;
            orders.Add(order);

            Console.WriteLine($"order added: {order}");

            var events = new List<OrderBookEvent> {new CreateOrderConfirmed(Security, Now(), order.ClientId, order.ToOrder())};
            events.AddRange(Match());

            if (order.Status == OrderStatus.Working)
            {
                order.ConvertToLimit();
            }

            FireEvents(events.ToArray());
        }

        public void UpdateLimitOrder(Guid clientId, Guid orderId, decimal price, int quantity)
        {
            if (ValidateUpdate(clientId, orderId, OrderRejectedReason.MarketClosed, 
                () => Status == OrderBookStatus.Closed)) return;
            if (ValidateUpdate(clientId, orderId, OrderRejectedReason.InvalidPriceIncrement,
                () => price % Security.TickSize != 0)) return;
            if (ValidateUpdate(clientId, orderId, OrderRejectedReason.InvalidQuantity, 
                () => quantity < 1)) return;
            if (ValidateUpdate(clientId, orderId, OrderRejectedReason.TooLateToCancel, 
                () => _completedOrders.ContainsKey(orderId))) return;
            if (ValidateUpdate(clientId, orderId, OrderRejectedReason.OrderNotInBook, 
                () => !_orders.ContainsKey(orderId))) return;
            var order = _orders[orderId];

            if (quantity <= order.FilledQuantity)
            {
                order.Cancel(Now());
                CompleteOrder(order);

                Console.WriteLine($"order cancelled on update as new quantity <= filled quantity: {order}");

                FireEvents(new CancelOrderConfirmed(Security, Now(), order.ClientId, order.ToOrder(),
                    OrderCancelledReason.UpdatedQuantityLowerThanFilledQuantity));
                return;
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

            var events = new List<OrderBookEvent> {new UpdateOrderConfirmed(Security, Now(), order.ClientId, order.ToOrder())};

            if (Status == OrderBookStatus.Open && isPriceChange)
            {
                events.AddRange(Match());
            }

            FireEvents(events.ToArray());
        }

        public void CancelOrder(Guid clientId, Guid orderId)
        {
            if (ValidateCancel(clientId, orderId, OrderRejectedReason.MarketClosed, 
                () => Status == OrderBookStatus.Closed)) return;
            if (ValidateCancel(clientId, orderId, OrderRejectedReason.TooLateToCancel, 
                () => _completedOrders.ContainsKey(orderId))) return;
            if (ValidateCancel(clientId, orderId, OrderRejectedReason.OrderNotInBook, 
                () => !_orders.ContainsKey(orderId))) return;
            var order = _orders[orderId];

            order.Cancel(Now());
            CompleteOrder(order);

            Console.WriteLine($"order cancelled: {order}");

            FireEvents(new CancelOrderConfirmed(Security, Now(), order.ClientId, order.ToOrder(), OrderCancelledReason.Cancelled));
        }

        private void FireEvents(params OrderBookEvent[] events)
        {
            OrderBookEvent?.Invoke(this, new OrderBookEventArgs(events));
        }

        private bool ValidateCreate(Guid clientId, Guid orderId, OrderRejectedReason reason, Func<bool> validation)
        {
            if (!validation.Invoke()) return false;

            FireEvents(new CreateOrderRejected(Security, Now(), clientId, orderId, reason));
            return true;
        }

        private bool ValidateUpdate(Guid clientId, Guid orderId, OrderRejectedReason reason, Func<bool> validation)
        {
            if (!validation.Invoke()) return false;

            FireEvents(new UpdateOrderRejected(Security, Now(), clientId, orderId, reason));
            return true;
        }

        private bool ValidateCancel(Guid clientId, Guid orderId, OrderRejectedReason reason, Func<bool> validation)
        {
            if (!validation.Invoke()) return false;

            FireEvents(new CancelOrderRejected(Security, Now(), clientId, orderId, reason));
            return true;
        }

        private OrderBookEvent ExpireOrder(InternalOrder order)
        {
            order.Expire(Now());
            CompleteOrder(order);

            Console.WriteLine($"order expired: {order}");

            return new ExpireOrderConfirmed(Security, Now(), order.ClientId, order.ToOrder());
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

                events.Add(new OrderMatched(Security,
                    time, price, quantity,
                    resting.ToOrder(),
                    aggressor.ToOrder()
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

        public void UpdateStatus(OrderBookStatus status)
        {
            switch (status)
            {
                case OrderBookStatus.PreOpen:
                    PreOpenMarket();
                    break;
                case OrderBookStatus.Open:
                    OpenMarket();
                    break;
                case OrderBookStatus.Closed:
                    CloseMarket();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(status), status, null);
            }
        }

        private void PreOpenMarket()
        {
            // TODO: need better system for multiple sessions per day
            var date = Now();
            _nextSequenceNumber = ((date.Year * 10000) + (date.Month * 100) + date.Day) * 10000000000L;
            Status = OrderBookStatus.PreOpen;
            FireEvents(new StatusChanged(Security, Now(), Status));
        }

        private void OpenMarket()
        {
            Status = OrderBookStatus.Open;
            var events = new List<OrderBookEvent> {new StatusChanged(Security, Now(), Status)};
            events.AddRange(Match());
            FireEvents(events.ToArray());
        }

        private void CloseMarket()
        {
            Status = OrderBookStatus.Closed;
            var events = new List<OrderBookEvent> {new StatusChanged(Security, Now(), Status)};
            events.AddRange(ExpireDayOrders());
            FireEvents(events.ToArray());
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