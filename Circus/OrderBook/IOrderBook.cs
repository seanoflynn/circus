using System;
using System.Collections.Generic;

namespace Circus.OrderBook
{
    public interface IOrderBook
    {
        Security Security { get; }

        OrderBookStatus Status { get; }

        IList<Level> GetLevels(Side side, int maxPrices);

        IList<OrderBookEvent> Process(OrderBookAction action);

        IList<OrderBookEvent> CreateLimitOrder(Guid clientId, Guid orderId, OrderValidity validity, Side side,
            decimal price,
            int quantity);

        IList<OrderBookEvent> CreateMarketOrder(Guid clientId, Guid orderId, OrderValidity validity, Side side,
            int quantity);

        IList<OrderBookEvent> UpdateLimitOrder(Guid clientId, Guid orderId, decimal price, int quantity);

        IList<OrderBookEvent> CancelOrder(Guid clientId, Guid id);

        IList<OrderBookEvent> UpdateStatus(OrderBookStatus status);
    }
}