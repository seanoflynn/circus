namespace Circus.Matching;

internal interface IMatchingAlgorithm
{
    bool TryQuoteIndicative(IReadOnlyDictionary<Side, IReadOnlyPriceLadder> working,
        out long priceTicks, out int quantity);

    bool TryBegin(IReadOnlyDictionary<Side, IReadOnlyPriceLadder> working);

    Allocation? SelectNext(InternalOrder restingHead, InternalOrder aggressor);

    bool UsesFullRemainingQuantity { get; }

    bool ChecksTradeRestrictions { get; }

    void OnTrade(long priceTicks);

    void OnSessionChange(long? referencePriceTicks);
}
