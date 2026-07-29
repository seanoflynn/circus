namespace Circus.OrderBook;

// A call-auction print: every trade clears at one price, sized off each side's full remaining
// quantity. The print is a single atomic allocation, not a sequence of continuous touches an
// iceberg would have to ration its displayed size across.
internal sealed class Auction : IMatchingAlgorithm
{
    // Feeds the tie-break below and nothing else. Seeded from an explicit reference price
    // (CME's settlement price, pre-open) and thereafter tracks the last trade.
    private long? _referencePriceTicks;

    // Held for the duration of one print, so trades are allocated against the book as it stood
    // when the price was struck rather than one they are themselves consuming.
    private long _clearingPriceTicks;

    public void OnTrade(long priceTicks) => _referencePriceTicks = priceTicks;

    public void OnSessionChange(long? referencePriceTicks) => _referencePriceTicks = referencePriceTicks;

    // Commits to the price already being quoted, so an uncrossing pass over an uncrossed book
    // declines here rather than needing the caller to check first.
    public bool TryBegin(IReadOnlyDictionary<Side, IReadOnlyPriceLadder> working) =>
        TryQuoteIndicative(working, out _clearingPriceTicks, out _);

    // Time priority, at the clearing price rather than each order's own.
    public Allocation? SelectNext(InternalOrder restingHead, InternalOrder aggressor) =>
        new Allocation(restingHead,
            Math.Min(restingHead.RemainingQuantity, aggressor.RemainingQuantity),
            _clearingPriceTicks);

    public bool UsesFullRemainingQuantity => true;

    // The print resolves a crossed book; a volatility pause must not interrupt it partway.
    public bool ChecksTradeRestrictions => false;

    // The price maximizing executable volume: min(cumulative bids at/above p, cumulative asks
    // at/below p). Ties break by minimum surplus (CME's rule), then proximity to the reference
    // price (no venue documents a case this granular), then CME's final rule - highest price
    // if the surplus is on the buy side, lowest if on the sell side.
    //
    // Stops are deliberately excluded, unlike CME's iterative stop-election loop; Matcher.Run
    // picks them up afterwards like any other trade.
    public bool TryQuoteIndicative(IReadOnlyDictionary<Side, IReadOnlyPriceLadder> working,
        out long priceTicks, out int quantity)
    {
        priceTicks = 0;
        quantity = 0;

        var buyLevels = working[Side.Buy].EnumerateFromBest()
            .Select(x => (Tick: x.Tick, Qty: SumRemaining(x.First))).ToList();
        var sellLevels = working[Side.Sell].EnumerateFromBest()
            .Select(x => (Tick: x.Tick, Qty: SumRemaining(x.First))).ToList();

        if (buyLevels.Count == 0 || sellLevels.Count == 0 || buyLevels[0].Tick < sellLevels[0].Tick)
            return false;

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

        // surplus on the buy side (positive) -> prefer the higher price; sell side -> lower
        return candidateSurplus > 0 ? candidatePrice > currentPrice : candidatePrice < currentPrice;
    }

    // Counts an iceberg's hidden reserve: price discovery is on true size, unlike the
    // displayed-only aggregates published as market data.
    private static int SumRemaining(InternalOrder? first)
    {
        var total = 0;
        for (var order = first; order != null; order = order.LevelNext)
            total += order.RemainingQuantity;
        return total;
    }
}
