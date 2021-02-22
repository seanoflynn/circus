using System;
using System.Collections.Generic;

namespace Circus
{
    public class Exchange
    {
        // public event EventHandler<OrderCreatedSuccessEventArgs> OrderCreated;
        // public event EventHandler<OrderUpdatedSuccessEventArgs> OrderUpdated;
        // public event EventHandler<OrderCancelledSuccessEventArgs> OrderCancelled;
        // public event EventHandler<OrderCreateRejectedEventArgs> OrderCreateRejected;
        // public event EventHandler<OrderUpdateRejectedEventArgs> OrderUpdateRejected;
        // public event EventHandler<OrderCancelRejectedEventArgs> OrderCancelRejected;
        // public event EventHandler<OrderFilledEventArgs> OrderFilled;
        // public event EventHandler<OrderExpiredEventArgs> OrderExpired;
        // public event EventHandler<TradedEventArgs> Traded;
        
        private readonly Dictionary<Security, OrderBook.OrderBook> _books = new();
        
        // private readonly Dictionary<Security, AggregateBook> _aggregateBooks = new();

        // private readonly Dictionary<Security, int> _incrementalSeqNums = new();

        // private TradingSession _session;

        // private static int NextOrderId;

        private readonly ITimeProvider _timeProvider;

        public Exchange(ITimeProvider timeProvider)
        {
            _timeProvider = timeProvider;
        }

        public void Start()
        {
            Console.WriteLine("exchange started");

            // snapshotTimer = new Timer(HandleSnapshotTimerInterval, null, 0, snapshotUpdateInterval);
        }

        public void Stop()
        {
            Console.WriteLine("exchange started");
        }

        public OrderBook.OrderBook GetBook(Security sec)
        {
            return _books[sec];
        }
        
        public void AddSecurity(Security sec)
        {
            var book = new OrderBook.OrderBook(sec, _timeProvider);
            // book.OrderCreated += HandleOrderCreateAccepted;
            // book.OrderUpdated += HandleOrderUpdateAccepted;
            // book.OrderDeleted += HandleOrderDeleteAccepted;
            // book.OrderCreateRejected += HandleOrderCreateRejected;
            // book.OrderUpdateRejected += HandleOrderUpdateRejected;
            // book.OrderDeleteRejected += HandleOrderDeleteRejected;
            // book.OrderFilled += HandleOrderFilled;
            // book.OrderExpired += HandleOrderExpired;
            // book.Traded += HandleTraded;
            //book.BookUpdated += HandleBookUpdated;

            _books.Add(sec, book);
            //
            // _incrementalSeqNums.Add(sec, 0);
            // _aggregateBooks.Add(sec, new AggregateBook(10));
        }

        // private void CreateOrder(OrderInfo order)
        // {
            // if (!_books.ContainsKey(request.Contract))
            // {
            //     ((FixTcpClient) sender).Send(
            //         new BusinessLevelReject(request, BusinessLevelRejectReason.UnknownSecurity));
            //     return;
            // }
            //
            // int id = NextOrderId++;
            // string globexId = id.ToString();
            // ordersToClients[id] = (FixTcpClient) sender;
            // _orderInfos[id] = new OrderInfo(globexId, request.ClientOrderId,
            //     request.CorrelationClientOrderId, request.Account,
            //     request.IsManualOrder, request.PreTradeAnonymity);
            // _orderIds[globexId] = id;
            //
            // var book = _books[request.Contract];
            //
            // int minQty = request.MinQuantity ?? 0;
            // int maxVisibleQty = request.MaxVisibleQuantity ?? int.MaxValue;
            // SelfMatchPreventionInstruction smpMode =
            //     request.SelfMatchPreventionInstruction ?? SelfMatchPreventionInstruction.CancelResting;
            //
            // switch (request.OrderType)
            // {
            //     case OrderType.Limit:
            //         book.CreateLimitOrder(id, request.TimeInForce, request.ExpireDate,
            //             request.Side, request.Price, request.Quantity,
            //             minQty, maxVisibleQty,
            //             request.SelfMatchPreventionId, smpMode);
            //         break;
            //     case OrderType.Market:
            //         book.CreateMarketOrder(id, request.TimeInForce, request.ExpireDate,
            //             request.Side, request.Quantity,
            //             minQty, maxVisibleQty,
            //             request.SelfMatchPreventionId, smpMode);
            //         break;
            //     case OrderType.MarketLimit:
            //         book.CreateMarketLimitOrder(id, request.TimeInForce, request.ExpireDate,
            //             request.Side, request.Quantity,
            //             minQty, maxVisibleQty,
            //             request.SelfMatchPreventionId, smpMode);
            //         break;
            //     case OrderType.Stop:
            //         book.CreateStopOrder(id, request.TimeInForce, request.ExpireDate,
            //             request.Side, request.StopPrice, request.Quantity,
            //             minQty, maxVisibleQty,
            //             request.SelfMatchPreventionId, smpMode);
            //         break;
            //     case OrderType.StopLimit:
            //         book.CreateStopLimitOrder(id, request.TimeInForce, request.ExpireDate,
            //             request.Side, request.Price, request.StopPrice, request.Quantity,
            //             minQty, maxVisibleQty,
            //             request.SelfMatchPreventionId, smpMode);
            //         break;
            // }
        // }

        // private void UpdateOrder(object sender, OrderInfo request)
        // {
            // if (!_books.ContainsKey(request.Contract))
            // {
            //     ((FixTcpClient) sender).Send(
            //         new BusinessLevelReject(request, BusinessLevelRejectReason.UnknownSecurity));
            //     return;
            // }
            //
            // var book = _books[request.Contract];
            // var id = _orderIds[request.OrderId];
            //
            // if (!book.Contains(id))
            // {
            //     ((FixTcpClient) sender).Send(new BusinessLevelReject(request, BusinessLevelRejectReason.UnknownId));
            //     return;
            // }
            //
            // int maxVisibleQty = request.MaxVisibleQuantity ?? int.MaxValue;
            //
            // switch (request.OrderType)
            // {
            //     case OrderType.Limit:
            //         book.UpdateLimitOrder(id, request.Price, request.Quantity, maxVisibleQty);
            //         break;
            //     case OrderType.Market:
            //         book.UpdateLimitOrder(id, request.Price, request.Quantity, maxVisibleQty);
            //         break;
            //     case OrderType.MarketLimit:
            //         book.UpdateLimitOrder(id, request.Price, request.Quantity, maxVisibleQty);
            //         break;
            //     case OrderType.Stop:
            //         book.UpdateLimitOrder(id, request.Price, request.Quantity, maxVisibleQty);
            //         break;
            //     case OrderType.StopLimit:
            //         book.UpdateLimitOrder(id, request.Price, request.Quantity, maxVisibleQty);
            //         break;
            // }
        // }

        // private void DeleteOrder(object sender, OrderInfo request)
        // {
            // if (!_books.ContainsKey(request.Contract))
            // {
            //     ((FixTcpClient) sender).Send(
            //         new BusinessLevelReject(request, BusinessLevelRejectReason.UnknownSecurity));
            //     return;
            // }
            //
            // var id = _orderIds[request.OrderId];
            //
            // if (!_books[request.Contract].Contains(id))
            // {
            //     ((FixTcpClient) sender).Send(new BusinessLevelReject(request, BusinessLevelRejectReason.UnknownId));
            //     return;
            // }
            //
            // _books[request.Contract].DeleteOrder(id);
        // }
    }
}