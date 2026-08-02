namespace Circus.Matching;

// How a trade is allocated and priced. Matcher owns the loop around this - the crossing
// condition, self-match detection, stop-triggering - and runs it identically whichever
// algorithm is active; only the decisions below vary. Instances rather than singletons, so an
// instrument can eventually carry its own set.
internal interface IMatchingAlgorithm
{
    // What this algorithm would print right now without committing to it, which is what
    // pre-open publishes as an indicative quote. Continuous trading has no single such price
    // and declines. Side-effect-free, unlike TryBegin.
    bool TryQuoteIndicative(IReadOnlyDictionary<Side, IReadOnlyPriceLadder> working,
        out long priceTicks, out int quantity);

    // Derives whatever run-scoped state the algorithm needs - an auction strikes its clearing
    // price here. False means there is nothing to match and the run yields nothing.
    bool TryBegin(IReadOnlyDictionary<Side, IReadOnlyPriceLadder> working);

    // The counterparty for the next trade, which is the decision separating one algorithm from
    // another. restingHead is the FIFO-earliest order at the resting side's best level; walk
    // LevelNext for the rest. The order returned must come from that level but need not be the
    // head - PriceLadder unlinks from any position, so pro-rata and friends are expressible.
    // Null ends the run; a crossed book normally must not decline.
    //
    // An algorithm caching per-level state should key it on the price tick and recompute when
    // the level changes: a selection can be dropped before it trades if self-match prevention
    // cancels the pair.
    Allocation? SelectNext(InternalOrder restingHead, InternalOrder aggressor);

    // Whether fills take an order's full remaining quantity rather than its displayed peak.
    bool UsesFullRemainingQuantity { get; }

    bool ChecksTradeRestrictions { get; }

    // Reference-price upkeep, mirroring IPriceRestriction. The book fans these across every
    // phase's algorithm, so one with no anchor ignores them.
    void OnTrade(long priceTicks);

    void OnSessionChange(long? referencePriceTicks);
}
