using System;
using System.Collections.Generic;

namespace Circus.OrderBook
{
    public interface IOrderBook
    {
        public event EventHandler<OrderBookEventArgs> OrderBookEvent;

        Security Security { get; }
        OrderBookStatus Status { get; }
        IList<Level> GetLevels(Side side, int maxPrices);

        void CreateLimitOrder(Guid id, TimeInForce tif, Side side, decimal price, int quantity);
        void CreateMarketOrder(Guid id, TimeInForce tif, Side side, int quantity);
        void UpdateLimitOrder(Guid id, decimal price, int quantity);
        void CancelOrder(Guid id);
        void SetStatus(OrderBookStatus status);
    }
}