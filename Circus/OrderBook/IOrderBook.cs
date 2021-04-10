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

        IList<OrderBookEvent> CreateOrder(Guid clientId, Guid orderId, OrderValidity validity, Side side, int quantity,
            decimal? price = null, decimal? triggerPrice = null);

        IList<OrderBookEvent> UpdateOrder(Guid clientId, Guid orderId, int? quantity = null, decimal? price = null,
            decimal? triggerPrice = null);

        IList<OrderBookEvent> CancelOrder(Guid clientId, Guid id);

        IList<OrderBookEvent> UpdateStatus(OrderBookStatus status);
    }
}