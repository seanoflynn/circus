using System.Collections.Generic;

namespace Circus.OrderBook
{
    public interface IOrderBook
    {
        Security Security { get; }

        OrderBookStatus Status { get; }

        IReadOnlyList<Level> GetLevels(Side side, int maxPrices);

        IReadOnlyList<OrderBookEvent> Process(OrderBookAction action);
    }
}
