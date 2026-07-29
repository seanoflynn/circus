namespace Circus.OrderBook;

// Continuous trading under price-time priority. The aggressor trades against the FIFO-earliest
// order at the best crossing level - taking the head and only moving on once it is consumed is
// the time-priority rule - at that order's own limit price, so an aggressor whose limit was
// better than the touch gets the improvement. Sized off displayed quantity, since an iceberg
// shows one peak at a time.
internal sealed class PriceTime : IMatchingAlgorithm
{
    public bool TryBegin(IReadOnlyDictionary<Side, IReadOnlyPriceLadder> working) => true;

    // Prints at as many prices as the sweep touches, and the best of them is the visible touch.
    public bool TryQuoteIndicative(IReadOnlyDictionary<Side, IReadOnlyPriceLadder> working,
        out long priceTicks, out int quantity)
    {
        priceTicks = 0;
        quantity = 0;
        return false;
    }

    public Allocation? SelectNext(InternalOrder restingHead, InternalOrder aggressor) =>
        new Allocation(restingHead,
            Math.Min(restingHead.DisplayedQuantity, aggressor.DisplayedQuantity),
            restingHead.Price ?? throw new InvalidOperationException("limit order requires price"));

    public bool UsesFullRemainingQuantity => false;

    public bool ChecksTradeRestrictions => true;

    // No anchor: every price comes from the two orders at the touch.
    public void OnTrade(long priceTicks)
    {
    }

    public void OnSessionChange(long? referencePriceTicks)
    {
    }
}
