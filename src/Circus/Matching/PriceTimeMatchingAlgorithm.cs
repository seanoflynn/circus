namespace Circus.Matching;

internal sealed class PriceTimeMatchingAlgorithm : IMatchingAlgorithm
{
    public bool TryBegin(IReadOnlyDictionary<Side, IReadOnlyPriceLadder> working) => true;

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

    public void OnTrade(long priceTicks)
    {
    }

    public void OnSessionChange(long? referencePriceTicks)
    {
    }
}
