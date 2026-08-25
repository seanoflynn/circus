namespace Circus.Matching;

internal sealed class AuctionMatchingAlgorithm : IMatchingAlgorithm
{
    private long? _referencePriceTicks;

    private long _clearingPriceTicks;

    public void OnTrade(long priceTicks) => _referencePriceTicks = priceTicks;

    public void OnSessionChange(long? referencePriceTicks) => _referencePriceTicks = referencePriceTicks;

    public bool TryBegin(IReadOnlyDictionary<Side, IReadOnlyPriceLadder> working) =>
        TryQuoteIndicative(working, out _clearingPriceTicks, out _);

    public Allocation? SelectNext(InternalOrder restingHead, InternalOrder aggressor) =>
        new Allocation(restingHead,
            Math.Min(restingHead.RemainingQuantity, aggressor.RemainingQuantity),
            _clearingPriceTicks);

    public bool UsesFullRemainingQuantity => true;

    public bool ChecksTradeRestrictions => false;

    public bool TryQuoteIndicative(IReadOnlyDictionary<Side, IReadOnlyPriceLadder> working,
        out long priceTicks, out int quantity)
    {
        priceTicks = 0;
        quantity = 0;

        if (!working[Side.Buy].TryGetBest(out var bestBid, out _) ||
            !working[Side.Sell].TryGetBest(out var bestAsk, out _) ||
            bestBid < bestAsk)
            return false;

        var buyLevels = working[Side.Buy].EnumerateFromBest()
            .Select(x => (Tick: x.Tick, Qty: SumRemaining(x.First))).ToList();
        var sellLevels = working[Side.Sell].EnumerateFromBest()
            .Select(x => (Tick: x.Tick, Qty: SumRemaining(x.First))).ToList();

        var candidates = buyLevels.Select(l => l.Tick).Concat(sellLevels.Select(l => l.Tick)).Distinct();

        (long Price, int Executable, long Surplus)? best = null;
        foreach (var p in candidates)
        {
            var cumBid = buyLevels.Where(l => l.Tick >= p).Sum(l => l.Qty);
            var cumAsk = sellLevels.Where(l => l.Tick <= p).Sum(l => l.Qty);
            var executable = Math.Min(cumBid, cumAsk);
            var surplus = cumBid - cumAsk;

            if (best == null || executable > best.Value.Executable ||
                (executable == best.Value.Executable &&
                 IsBetterTieBreak(p, surplus, best.Value.Price, best.Value.Surplus)))
            {
                best = (p, executable, surplus);
            }
        }

        priceTicks = best!.Value.Price;
        quantity = best.Value.Executable;
        return quantity > 0;
    }

    private bool IsBetterTieBreak(long candidatePrice, long candidateSurplus, long currentPrice,
        long currentSurplus)
    {
        var candidateAbsSurplus = Math.Abs(candidateSurplus);
        var currentAbsSurplus = Math.Abs(currentSurplus);
        if (candidateAbsSurplus != currentAbsSurplus)
            return candidateAbsSurplus < currentAbsSurplus;

        if (_referencePriceTicks.HasValue)
        {
            var candidateDistance = Math.Abs(candidatePrice - _referencePriceTicks.Value);
            var currentDistance = Math.Abs(currentPrice - _referencePriceTicks.Value);
            if (candidateDistance != currentDistance)
                return candidateDistance < currentDistance;
        }

        return candidateSurplus > 0 ? candidatePrice > currentPrice : candidatePrice < currentPrice;
    }

    private static int SumRemaining(InternalOrder? first)
    {
        var total = 0;
        for (var order = first; order != null; order = order.LevelNext)
            total += order.RemainingQuantity;
        return total;
    }
}
