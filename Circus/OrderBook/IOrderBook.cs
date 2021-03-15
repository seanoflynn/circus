using System;
using System.Collections.Generic;

namespace Circus.OrderBook
{
    public interface IOrderBook
    {
        public event EventHandler<OrderBookEventArgs> OrderBookEvent;

        void Process(OrderBookAction action);

        Security Security { get; }
        OrderBookStatus Status { get; }
        IList<Level> GetLevels(Side side, int maxPrices);

        void CreateLimitOrder(Guid clientId, Guid orderId, OrderValidity validity, Side side, decimal price,
            int quantity);

        void CreateMarketOrder(Guid clientId, Guid orderId, OrderValidity validity, Side side, int quantity);

        void UpdateLimitOrder(Guid clientId, Guid orderId, decimal price, int quantity);

        void CancelOrder(Guid clientId, Guid id);

        void UpdateStatus(OrderBookStatus status);
    }
}