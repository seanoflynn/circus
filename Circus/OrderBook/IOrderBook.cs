using System.Collections.Generic;

namespace Circus.OrderBook
{
    public interface IOrderBook
    {
        Security Security { get; }

        OrderBookStatus Status { get; }

        IReadOnlyList<Level> GetLevels(Side side, int maxPrices);

        // Live indicative auction price during PreOpen (or a mid-session volatility pause) - the
        // price/quantity that would result if the auction ended right now. Recalculated from the
        // current book on every call, so it naturally tracks order entry/cancellation.
        bool TryGetIndicativeAuctionPrice(out decimal price, out int quantity);

        IReadOnlyList<OrderBookEvent> Process(OrderBookAction action);
    }
}
