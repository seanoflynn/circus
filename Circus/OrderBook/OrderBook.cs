using System;
using System.Collections.Generic;
using System.Linq;
using Circus.Enums;

// TODO:
// - Exchange.AddSecurity(Security, MatchingEngine)
// - Move matching logic to matching engine class
// - Exchange has Map<Security,OrderBook>

namespace Circus.OrderBook
{
    public class OrderBook
    {
        public event EventHandler<OrderCreatedSuccessEventArgs> OrderCreated;
        public event EventHandler<OrderUpdatedSuccessEventArgs> OrderUpdated;
        public event EventHandler<OrderDeletedSuccessEventArgs> OrderDeleted;

        public event EventHandler<OrderCreateRejectedEventArgs> OrderCreateRejected;
        public event EventHandler<OrderUpdateRejectedEventArgs> OrderUpdateRejected;
        public event EventHandler<OrderDeleteRejectedEventArgs> OrderDeleteRejected;

        public event EventHandler<OrderFilledEventArgs> OrderFilled;
        public event EventHandler<OrderExpiredEventArgs> OrderExpired;

        public event EventHandler<TradedEventArgs> Traded;
        public event EventHandler<BookUpdatedEventArgs> Updated;

        public Security Security { get; }
        public OrderBookStatus Status { get; private set; } = OrderBookStatus.Closed;

        private readonly ITimeProvider _timeProvider;
        private DateTime Now() => _timeProvider.GetCurrentTime();

        private class DescendingComparer : IComparer<decimal>
        {
            public int Compare(decimal x, decimal y)
            {
                return y.CompareTo(x);
            }
        }

        private readonly SortedDictionary<decimal, List<InternalOrder>> _buyOrders = new(new DescendingComparer());
        private readonly SortedDictionary<decimal, List<InternalOrder>> _sellOrders = new();
        private readonly Dictionary<Guid, InternalOrder> _orders = new();
        private readonly Dictionary<Guid, InternalOrder> _completedOrders = new();

        public OrderBook(Security security, ITimeProvider timeProvider)
        {
            Security = security;
            _timeProvider = timeProvider;
        }

        public void CreateLimitOrder(Guid id, TimeInForce tif, Side side, decimal price, int quantity)
        {
            if (ValidateCreate(id, RejectReason.MarketClosed, () => Status == OrderBookStatus.Closed)) return;
            if (ValidateCreate(id, RejectReason.InvalidPriceIncrement, () => price % Security.TickSize != 0)) return;
            if (ValidateCreate(id, RejectReason.InvalidQuantity, () => quantity < 1)) return;

            var order = new InternalOrder(id, Security, Now(), tif, side, price, quantity);

            _orders.Add(order.Id, order);

            var orders = order.Side == Side.Buy ? _buyOrders : _sellOrders;
            orders.Add(order);

            Console.WriteLine($"order added to book: {order}");
            OrderCreated?.Invoke(this, new OrderCreatedSuccessEventArgs(order.ToOrder()));

            Match();
        }

        public void UpdateLimitOrder(Guid id, decimal price, int quantity)
        {
            if (ValidateUpdate(id, RejectReason.MarketClosed, () => Status == OrderBookStatus.Closed)) return;
            if (ValidateUpdate(id, RejectReason.InvalidPriceIncrement, () => price % Security.TickSize != 0)) return;
            if (ValidateUpdate(id, RejectReason.InvalidQuantity, () => quantity < 1)) return;
            if (ValidateUpdate(id, RejectReason.TooLateToCancel, () => _completedOrders.ContainsKey(id))) return;
            if (ValidateUpdate(id, RejectReason.OrderNotInBook, () => !_orders.ContainsKey(id))) return;
            var order = _orders[id];

            // TODO: cancel order if quantity < remaining quantity

            var isPriceChange = price != order.Price;

            var orders = order.Side == Side.Buy ? _buyOrders : _sellOrders;
            if (isPriceChange)
            {
                orders.Remove(order);
            }

            order.Update(Now(), price, quantity);
            if (isPriceChange)
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

        public void DeleteOrder(Guid id)
        {
            if (ValidateDelete(id, RejectReason.MarketClosed, () => Status == OrderBookStatus.Closed)) return;
            if (ValidateDelete(id, RejectReason.TooLateToCancel, () => _completedOrders.ContainsKey(id))) return;
            if (ValidateDelete(id, RejectReason.OrderNotInBook, () => !_orders.ContainsKey(id))) return;
            var order = _orders[id];

            order.Delete(Now());
            CompleteOrder(order);

            Console.WriteLine($"order deleted from book: {order}");

            OrderDeleted?.Invoke(this, new OrderDeletedSuccessEventArgs(order.ToOrder(), OrderDeletedReason.Client));
        }

        private void CompleteOrder(InternalOrder order)
        {
            var orders = order.Side == Side.Buy ? _buyOrders : _sellOrders;
            orders[order.Price].Remove(order);
            _orders.Remove(order.Id);
            _completedOrders.Add(order.Id, order);
        }

        private bool ValidateCreate(Guid id, RejectReason reason, Func<bool> validation)
        {
            if (!validation.Invoke()) return false;

            OrderCreateRejected?.Invoke(this, new OrderCreateRejectedEventArgs(id, reason));
            return true;
        }

        private bool ValidateUpdate(Guid id, RejectReason reason, Func<bool> validation)
        {
            if (!validation.Invoke()) return false;

            OrderUpdateRejected?.Invoke(this, new OrderUpdateRejectedEventArgs(id, reason));
            return true;
        }

        private bool ValidateDelete(Guid id, RejectReason reason, Func<bool> validation)
        {
            if (!validation.Invoke()) return false;

            OrderDeleteRejected?.Invoke(this, new OrderDeleteRejectedEventArgs(id, reason));
            return true;
        }

        private void Match()
        {
            var fills = new List<Fill>();
            var time = Now();

            var buy = _buyOrders.FirstOrDefault().Value?.FirstOrDefault();
            var sell = _sellOrders.FirstOrDefault().Value?.FirstOrDefault();

            while (buy != null && sell != null && buy.Price >= sell.Price)
            {
                var resting = buy.CreatedTime < sell.CreatedTime ? buy : sell;
                var aggressor = buy == resting ? sell : buy;

                fills.AddRange(Match(time, resting, aggressor));

                buy = _buyOrders.FirstOrDefault().Value?.FirstOrDefault();
                sell = _sellOrders.FirstOrDefault().Value?.FirstOrDefault();
            }

            if (fills.Count > 0)
            {
                Traded?.Invoke(this, new TradedEventArgs(fills));
            }
        }

        private IEnumerable<Fill> Match(DateTime time, InternalOrder resting, InternalOrder aggressing)
        {
            var quantity = Math.Min(resting.Quantity, aggressing.Quantity);
            var price = resting.Price;

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
            // TODO: validate transitions

            if (status == OrderBookStatus.Closed)
            {
                // ExpireAllOrders();
            }

            Status = status;

            Match();
        }

        // private void ExpireAllOrders()
        // {
        //     foreach (var order in _orders)
        //     {
        //         if (order.TimeInForce == TimeInForce.GoodTilCancel ||
        //             order.TimeInForce == TimeInForce.GoodTilDate)
        //             continue;
        //
        //         // order.RemainingQuantity = 0;
        //         // order.Status = OrderStatus.Expired;
        //
        //         OrderExpired?.Invoke(this, new OrderExpiredEventArgs(order.ToOrder()));
        //     }
        // }

        // public AggregateBook GetAggregateBook(int depth)
        // {
        //     var bids = WorkingOrders(Side.Buy).GroupBy(x => x.Price)
        //         .Take(depth)
        //         .Select(x => new AggregateBookLevel(x.Key.Value, x.Sum(y => y.Quantity), x.Count()));
        //
        //     var asks = WorkingOrders(Side.Sell).GroupBy(x => x.Price)
        //         .Take(depth)
        //         .Select(x => new AggregateBookLevel(x.Key.Value, x.Sum(y => y.Quantity), x.Count()));
        //
        //     var ab = new AggregateBook(depth, bids, asks);
        //
        //     return ab;
        // }


        // private int? _lastTradePrice;
        // private int _sessionVolume;
        // private int? _sessionMaxTradePrice;
        // private int? _sessionMinTradePrice;
        // private int? _sessionMaxBidPrice;
        // private int? _sessionMinAskPrice;
        // private int? _sessionOpenPrice;
        //
        // public DateTime SessionDate { get; private set; }
        //
        // public int? LastTradePrice
        // {
        //     get { return _lastTradePrice; }
        // }
        //
        // public int SessionVolume
        // {
        //     get { return _sessionVolume; }
        // }
        //
        // public int? SessionMaxTradePrice
        // {
        //     get { return _sessionMaxTradePrice; }
        // }
        //
        // public int? SessionMinTradePrice
        // {
        //     get { return _sessionMinTradePrice; }
        // }
        //
        // public int? SessionMaxBidPrice
        // {
        //     get { return _sessionMaxBidPrice; }
        // }
        //
        // public int? SessionMinAskPrice
        // {
        //     get { return _sessionMinAskPrice; }
        // }
        //
        // public int? SessionOpenPrice
        // {
        //     get { return _sessionOpenPrice; }
        // }
        //
        // public DateTime PreviousSessionDate { get; private set; }
        // public int PreviousSessionVolume { get; private set; }
        // public int PreviousSessionOpenInterest { get; private set; }
        // public int PreviousSessionSettlementPrice { get; private set; }


        // private bool IsWorking(Side side)
        // {
        //     return _orders.Any(x => (x.Status & OrderStatus.Working) != 0 && x.Side == side);
        // }
        //
        // public bool Contains(int id)
        // {
        //     return _orders.Any(x => x.Id == id);
        // }


        // public void CreateMarketOrder(Guid id, TimeInForce tif, Side side, int quantity)
        // {
        //     if (quantity < 1)
        //     {
        //         FireCreateRejected(id, OrderRejectReason.QuantityTooLow);
        //         return;
        //     }
        //
        //     if (Status != OrderBookStatus.Open)
        //     {
        //         FireCreateRejected(id, OrderRejectReason.MarketClosed);
        //         return;
        //     }
        //
        //     // TODO: reject if for market-limit, "A designated limit is farther than price bands from current Last Best Price"
        //
        //     var top = WorkingOrders(side == Side.Buy ? Side.Sell : Side.Buy).FirstOrDefault();
        //
        //     // check if book is empty
        //     if (top == null)
        //     {
        //         FireCreateRejected(id, OrderRejectReason.NoOrdersToMatchMarketOrder);
        //         return;
        //     }
        //
        //     // TODO: need to get these Protection Point values from somewhere
        //     // 		 -- "Protection point values usually equal half of the Non-reviewable range"
        //     var protectionPoints = 10 * (side == Side.Buy ? 1 : -1);
        //     var price = top.Price + protectionPoints;
        //
        //     var order = new Order(id, DateTime.Now, DateTime.Now, OrderStatus.Created, Security,
        //         OrderType.Market, tif, side, price, null, quantity);
        //
        //     // if (order.TimeInForce == TimeInForce.FillAndKill)
        //     // {
        //     //     // TODO: catch earlier?
        //     //     if (order.MinQuantity > order.Quantity)
        //     //     {
        //     //         FireCreateRejected(order, OrderRejectReason.QuantityOutOfRange);
        //     //         return;
        //     //     }
        //     // }
        //
        //     CreateOrder(order);
        //     Match();
        // }
        //
        // public void CreateMarketLimitOrder(Guid id, TimeInForce tif, Side side, int quantity)
        // {
        //     if (quantity < 1)
        //     {
        //         FireCreateRejected(id, OrderRejectReason.QuantityTooLow);
        //         return;
        //     }
        //
        //     if (Status != OrderBookStatus.Open)
        //     {
        //         FireCreateRejected(id, OrderRejectReason.MarketClosed);
        //         return;
        //     }
        //
        //     // TODO: reject if for market-limit, "A designated limit is farther than price bands from current Last Best Price"
        //
        //     var top = WorkingOrders(side == Side.Buy ? Side.Sell : Side.Buy).FirstOrDefault();
        //
        //     // check if book is empty
        //     if (top == null)
        //     {
        //         FireCreateRejected(id, OrderRejectReason.NoOrdersToMatchMarketOrder);
        //         return;
        //     }
        //
        //     var order = new Order(id, DateTime.Now, DateTime.Now, OrderStatus.Created, Security,
        //         OrderType.MarketLimit, tif, side, top.Price, null,
        //         quantity);
        //
        //
        //     // if (order.TimeInForce == TimeInForce.FillAndKill)
        //     // {
        //     //     // TODO: catch earlier?
        //     //     if (order.MinQuantity > order.Quantity)
        //     //     {
        //     //         FireCreateRejected(order, OrderRejectReason.QuantityOutOfRange);
        //     //         return;
        //     //     }
        //     // }
        //
        //     CreateOrder(order);
        //     Match();
        // }
        //
        // public void CreateStopMarketOrder(Guid id, TimeInForce tif, Side side, int stopPrice, int quantity)
        // {
        //     if (quantity < 1)
        //     {
        //         FireCreateRejected(id, OrderRejectReason.QuantityTooLow);
        //         return;
        //     }
        //
        //     if (Status == OrderBookStatus.Close || Status == OrderBookStatus.NotAvailable)
        //     {
        //         FireCreateRejected(id, OrderRejectReason.MarketClosed);
        //         return;
        //     }
        //
        //     // if (order.Side == Side.Buy && stopPrice > _lastTradePrice)
        //     // {
        //     //     FireCreateRejected(order.Id, OrderRejectReason.StopPriceMustBeLessThanLastTradePrice);
        //     //     return;
        //     // }
        //     //
        //     // if (order.Side == Side.Sell && stopPrice < _lastTradePrice)
        //     // {
        //     //     FireCreateRejected(order.Id, OrderRejectReason.StopPriceMustBeGreaterThanLastTradePrice);
        //     //     return;
        //     // }
        //
        //     // if (order.TimeInForce == TimeInForce.FillAndKill)
        //     // {
        //     //     // TODO: catch earlier?
        //     //     if (order.MinQuantity > order.Quantity)
        //     //     {
        //     //         FireCreateRejected(order, OrderRejectReason.QuantityOutOfRange);
        //     //         return;
        //     //     }
        //     // }
        //     
        //     // TODO: price doesn't get set until order is triggered
        //     var order = new Order(id, DateTime.Now, DateTime.Now, OrderStatus.Hidden, Security,
        //         OrderType.MarketLimit, tif, side, 0, stopPrice,
        //         quantity);
        //
        //     CreateOrder(order);
        //     Match();
        // }
        //
        // public void CreateStopLimitOrder(Guid id, TimeInForce tif, Side side, int price, int stopPrice, int quantity)
        // {
        //     var order = new Order(id, DateTime.Now, DateTime.Now, OrderStatus.Hidden, Security,
        //         OrderType.MarketLimit, tif, side, price, stopPrice,
        //         quantity);
        //
        //     if (quantity < 1)
        //     {
        //         FireCreateRejected(order.Id, OrderRejectReason.QuantityTooLow);
        //         return;
        //     }
        //
        //     if (Status == OrderBookStatus.Close || Status == OrderBookStatus.NotAvailable)
        //     {
        //         FireCreateRejected(order.Id, OrderRejectReason.MarketClosed);
        //         return;
        //     }
        //
        //     // if (order.Side == Side.Buy && stopPrice > _lastTradePrice)
        //     // {
        //     //     FireCreateRejected(order.Id, OrderRejectReason.StopPriceMustBeLessThanLastTradePrice);
        //     //     return;
        //     // }
        //     //
        //     // if (order.Side == Side.Sell && stopPrice < _lastTradePrice)
        //     // {
        //     //     FireCreateRejected(order.Id, OrderRejectReason.StopPriceMustBeGreaterThanLastTradePrice);
        //     //     return;
        //     // }
        //
        //     if (order.Type == OrderType.StopLimit)
        //     {
        //         if (order.Side == Side.Buy && price < stopPrice)
        //         {
        //             FireCreateRejected(order.Id, OrderRejectReason.InvalidStopPriceMustBeGreaterThanEqualTriggerPrice);
        //             return;
        //         }
        //
        //         if (order.Side == Side.Sell && price > stopPrice)
        //         {
        //             FireCreateRejected(order.Id, OrderRejectReason.InvalidStopPriceMustBeLessThanEqualTriggerPrice);
        //             return;
        //         }
        //     }
        //
        //     // if (order.TimeInForce == TimeInForce.FillAndKill)
        //     // {
        //     //     // TODO: catch earlier?
        //     //     if (order.MinQuantity > order.Quantity)
        //     //     {
        //     //         FireCreateRejected(order, OrderRejectReason.QuantityOutOfRange);
        //     //         return;
        //     //     }
        //     // }
        //
        //     CreateOrder(order);
        //     Match();
        // }
    }

    internal static class SortedDictionaryExtensions
    {
        internal static void Add(this SortedDictionary<decimal, List<InternalOrder>> orders, InternalOrder order)
        {
            if (orders.ContainsKey(order.Price))
            {
                orders[order.Price].Add(order);
            }
            else
            {
                orders[order.Price] = new List<InternalOrder> {order};
            }
        }

        internal static void Remove(this SortedDictionary<decimal, List<InternalOrder>> orders, InternalOrder order)
        {
            orders[order.Price].Remove(order);

            if (orders[order.Price].Count == 0)
            {
                orders.Remove(order.Price);
            }
        }
    }
}