using System;
using System.Collections.Generic;
using System.Linq;

namespace Circus.OrderBook
{
    public class OrderBook
    {
        public event EventHandler<OrderCreatedSuccessEventArgs> OrderCreated;
        public event EventHandler<OrderUpdatedSuccessEventArgs> OrderUpdated;
        public event EventHandler<OrderCancelledSuccessEventArgs> OrderCancelled;

        public event EventHandler<OrderCreateRejectedEventArgs> OrderCreateRejected;
        public event EventHandler<OrderUpdateRejectedEventArgs> OrderUpdateRejected;
        public event EventHandler<OrderCancelRejectedEventArgs> OrderCancelRejected;

        public event EventHandler<OrderFilledEventArgs> OrderFilled;
        public event EventHandler<OrderExpiredEventArgs> OrderExpired;

        public event EventHandler<TradedEventArgs> Traded;

        public Security Security { get; }
        public OrderBookStatus Status { get; private set; } = OrderBookStatus.Closed;

        private readonly ITimeProvider _timeProvider;
        private DateTime Now() => _timeProvider.GetCurrentTime();

        private long _nextSequenceNumber;

        private class DescendingComparer : IComparer<decimal>
        {
            public int Compare(decimal x, decimal y)
            {
                return y.CompareTo(x);
            }
        }

        private readonly SortedDictionary<decimal, SortedDictionary<long, InternalOrder>> _buyOrders =
            new(new DescendingComparer());

        private readonly SortedDictionary<decimal, SortedDictionary<long, InternalOrder>> _sellOrders = new();
        private readonly Dictionary<Guid, InternalOrder> _orders = new();
        private readonly Dictionary<Guid, InternalOrder> _completedOrders = new();

        public OrderBook(Security security, ITimeProvider timeProvider)
        {
            Security = security;
            _timeProvider = timeProvider;
        }

        public void CreateLimitOrder(Guid id, TimeInForce tif, Side side, decimal price, int quantity)
        {
            if (ValidateCreate(id, OrderRejectedReason.MarketClosed, () => Status == OrderBookStatus.Closed)) return;
            if (ValidateCreate(id, OrderRejectedReason.InvalidPriceIncrement,
                () => price % Security.TickSize != 0)) return;
            if (ValidateCreate(id, OrderRejectedReason.InvalidQuantity, () => quantity < 1)) return;

            _nextSequenceNumber++;
            var order = new InternalOrder(_nextSequenceNumber, id, Security, Now(), tif, side, price, quantity);

            _orders.Add(order.Id, order);

            var orders = order.Side == Side.Buy ? _buyOrders : _sellOrders;
            orders.Add(order);

            Console.WriteLine($"order added to book: {order}");
            OrderCreated?.Invoke(this, new OrderCreatedSuccessEventArgs(order.ToOrder()));

            Match();
        }

        public void CreateMarketOrder(Guid id, TimeInForce tif, Side side, int quantity)
        {
            if (ValidateCreate(id, OrderRejectedReason.MarketClosed, () => Status == OrderBookStatus.Closed)) return;
            if (ValidateCreate(id, OrderRejectedReason.InvalidQuantity, () => quantity < 1)) return;

            var oppositeOrders = (side == Side.Buy ? _sellOrders : _buyOrders);
            if (ValidateCreate(id, OrderRejectedReason.NoOrdersToMatchMarketOrder, () => !oppositeOrders.Any())) return;

            // set price as best offer + protection ticks for buy orders, best bid - protection ticks for sell orders
            // TODO: option to use best bid + protection tickets for buy orders, etc (eurex)
            var price = oppositeOrders.First().Key +
                        ((side == Side.Buy ? 1 : -1) * (Security.MarketOrderProtectionTicks * Security.TickSize));

            _nextSequenceNumber++;
            var order = new InternalOrder(_nextSequenceNumber, id, Security, Now(), tif, side, price, quantity);

            _orders.Add(order.Id, order);

            var orders = order.Side == Side.Buy ? _buyOrders : _sellOrders;
            orders.Add(order);

            Console.WriteLine($"order added to book: {order}");
            OrderCreated?.Invoke(this, new OrderCreatedSuccessEventArgs(order.ToOrder()));

            Match();
        }

        public void UpdateLimitOrder(Guid id, decimal price, int quantity)
        {
            if (ValidateUpdate(id, OrderRejectedReason.MarketClosed, () => Status == OrderBookStatus.Closed)) return;
            if (ValidateUpdate(id, OrderRejectedReason.InvalidPriceIncrement,
                () => price % Security.TickSize != 0)) return;
            if (ValidateUpdate(id, OrderRejectedReason.InvalidQuantity, () => quantity < 1)) return;
            if (ValidateUpdate(id, OrderRejectedReason.TooLateToCancel, () => _completedOrders.ContainsKey(id))) return;
            if (ValidateUpdate(id, OrderRejectedReason.OrderNotInBook, () => !_orders.ContainsKey(id))) return;
            var order = _orders[id];

            if (quantity <= order.FilledQuantity)
            {
                order.Cancel(Now());
                CompleteOrder(order);

                Console.WriteLine($"order cancelled on update as new quantity <= filled quantity: {order}");

                OrderCancelled?.Invoke(this,
                    new OrderCancelledSuccessEventArgs(order.ToOrder(),
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

            Console.WriteLine($"order updated in book: {order}");
            OrderUpdated?.Invoke(this, new OrderUpdatedSuccessEventArgs(order.ToOrder()));

            if (isPriceChange)
            {
                Match();
            }
        }

        public void CancelOrder(Guid id)
        {
            if (ValidateCancel(id, OrderRejectedReason.MarketClosed, () => Status == OrderBookStatus.Closed)) return;
            if (ValidateCancel(id, OrderRejectedReason.TooLateToCancel, () => _completedOrders.ContainsKey(id))) return;
            if (ValidateCancel(id, OrderRejectedReason.OrderNotInBook, () => !_orders.ContainsKey(id))) return;
            var order = _orders[id];

            order.Cancel(Now());
            CompleteOrder(order);

            Console.WriteLine($"order cancelled: {order}");

            OrderCancelled?.Invoke(this,
                new OrderCancelledSuccessEventArgs(order.ToOrder(), OrderCancelledReason.Cancelled));
        }

        private void ExpireOrder(InternalOrder order)
        {
            order.Expire(Now());
            CompleteOrder(order);

            Console.WriteLine($"order expired: {order}");

            OrderExpired?.Invoke(this, new OrderExpiredEventArgs(order.ToOrder()));
        }

        private void CompleteOrder(InternalOrder order)
        {
            var orders = order.Side == Side.Buy ? _buyOrders : _sellOrders;
            orders.Remove(order);
            _orders.Remove(order.Id);
            _completedOrders.Add(order.Id, order);
        }

        private bool ValidateCreate(Guid id, OrderRejectedReason reason, Func<bool> validation)
        {
            if (!validation.Invoke()) return false;

            OrderCreateRejected?.Invoke(this, new OrderCreateRejectedEventArgs(id, reason));
            return true;
        }

        private bool ValidateUpdate(Guid id, OrderRejectedReason reason, Func<bool> validation)
        {
            if (!validation.Invoke()) return false;

            OrderUpdateRejected?.Invoke(this, new OrderUpdateRejectedEventArgs(id, reason));
            return true;
        }

        private bool ValidateCancel(Guid id, OrderRejectedReason reason, Func<bool> validation)
        {
            if (!validation.Invoke()) return false;

            OrderCancelRejected?.Invoke(this, new OrderCancelRejectedEventArgs(id, reason));
            return true;
        }

        private void Match()
        {
            var fills = new List<Fill>();
            var time = Now();

            var buy = _buyOrders.FirstOrDefault().Value?.FirstOrDefault().Value;
            var sell = _sellOrders.FirstOrDefault().Value?.FirstOrDefault().Value;

            while (buy != null && sell != null && buy.Price >= sell.Price)
            {
                var resting = buy.CreatedTime < sell.CreatedTime ? buy : sell;
                var aggressor = buy == resting ? sell : buy;

                fills.AddRange(Match(time, resting, aggressor));

                buy = _buyOrders.FirstOrDefault().Value?.FirstOrDefault().Value;
                sell = _sellOrders.FirstOrDefault().Value?.FirstOrDefault().Value;
            }

            if (fills.Count > 0)
            {
                Traded?.Invoke(this, new TradedEventArgs(fills));
            }
        }

        private IEnumerable<Fill> Match(DateTime time, InternalOrder resting, InternalOrder aggressing)
        {
            var quantity = Math.Min(resting.RemainingQuantity, aggressing.RemainingQuantity);
            var price = resting.Price;

            Console.WriteLine($"matched orders: {quantity}@{price}");
            Console.WriteLine($"- resting    {resting}");
            Console.WriteLine($"- aggressing {aggressing}");

            var fill1 = FillOrder(resting, time, price, quantity, false);
            var fill2 = FillOrder(aggressing, time, price, quantity, true);

            OrderFilled?.Invoke(this, new OrderFilledEventArgs(fill1));
            OrderFilled?.Invoke(this, new OrderFilledEventArgs(fill2));

            return new[] {fill1, fill2};
        }

        private Fill FillOrder(InternalOrder order, DateTime time, decimal price, int quantity, bool isAggressor)
        {
            order.Fill(time, quantity);
            if (order.Status == OrderStatus.Filled)
            {
                CompleteOrder(order);
            }

            return new Fill(order.ToOrder(), time, price, quantity, isAggressor);
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

        public void SetStatus(OrderBookStatus status)
        {
            switch (status)
            {
                case OrderBookStatus.Closed:
                    CloseMarket();
                    break;
                case OrderBookStatus.Open:
                    OpenMarket();
                    break;
            }
        }

        private void OpenMarket()
        {
            ValidateSetStatus("cannot open market, market must in closed state",
                () => Status != OrderBookStatus.Closed);
            // TODO: move to closed -> pre open transition, consider scenario where market opens multiple times per day
            var date = Now();
            _nextSequenceNumber = ((date.Year * 10000) + (date.Month * 100) + date.Day) * 10000000000L;
            Status = OrderBookStatus.Open;
            Match();
        }

        private void CloseMarket()
        {
            ValidateSetStatus("cannot close market, market must in open state", () => Status != OrderBookStatus.Open);
            Status = OrderBookStatus.Closed;
            ExpireDayOrders();
        }

        private void ValidateSetStatus(string message, Func<bool> validation)
        {
            if (validation.Invoke())
            {
                throw new Exception($"error changing market state: {message}");
            }
        }

        private void ExpireDayOrders()
        {
            var orders = _orders.Values.Where(o => o.TimeInForce == TimeInForce.Day).ToList();
            foreach (var order in orders)
            {
                ExpireOrder(order);
            }
        }
    }

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