using Circus.Events;

namespace Circus.Restrictions;

// The restrictions one book enforces, and the policy for consulting them.
//
// A restriction answers one question - does this price stand - and says what it would cost if it
// does not. What to do with those answers is a separate matter and lives here: which restrictions
// a question goes to, whether the first refusal decides it or the severest, and how severity is
// ranked. The book asks five questions and broadcasts three events, and never iterates anything.
//
// Two rules, and they differ for a reason rather than by accident:
//
//   First wins, at order entry and at resumption. An entry refusal only has to name a rejection,
//   and a band and a daily limit turn an order away for reasons that read differently to whoever
//   sent it - so there is nothing to rank, only something to say. At resumption every refusal is
//   asking for the same thing, so the first is as good as any.
//
//   Severest wins, at trade time. A breach stops the market, and how it stops it matters: the
//   order restrictions happen to be declared in must not decide whether a breach that halts is
//   served or shadowed by one that merely pauses.
internal sealed class RestrictionSet
{
    private readonly IReadOnlyList<IPriceRestriction> _restrictions;

    // From an instrument's configuration, which is the ordinary path.
    public RestrictionSet(IReadOnlyList<PriceRestriction>? configs)
        : this(Adapt(configs))
    {
    }

    // Enforcement supplied outright rather than derived from a description. The seam OrderBook's
    // internal constructor exists for: combinations an Instrument cannot yet describe - two
    // trade-scoped restrictions disagreeing about severity, say - can still be assembled.
    public RestrictionSet(IReadOnlyList<IPriceRestriction> restrictions) =>
        _restrictions = restrictions;

    // Config in, enforcement out. The instrument describes what it trades under; this is the only
    // place that knows which adapter each description means, so a new restriction is a new arm
    // rather than a change to how books are constructed.
    private static IReadOnlyList<IPriceRestriction> Adapt(IReadOnlyList<PriceRestriction>? configs) =>
        configs == null
            ? Array.Empty<IPriceRestriction>()
            : configs.Select<PriceRestriction, IPriceRestriction>(config => config switch
            {
                OrderPriceBand band => new OrderPriceBandRestriction(band.BandTicks),
                VolatilityBand band => new VolatilityBandRestriction(band.RangeTicks, band.PauseFor,
                    band.Window, band.ExtendedRangeTicks),
                StaticPriceRange range => new StaticPriceRangeRestriction(range.RangeTicks, range.PauseFor),

                // Same adapter as VolatilityBand: a velocity limit is that range at a short
                // window, and the two configs exist to say which is meant, not to behave apart.
                VelocityLimit limit => new VolatilityBandRestriction(limit.RangeTicks, limit.PauseFor,
                    limit.Window),
                DailyPriceLimit limit => new DailyPriceLimitRestriction(limit.Width),
                CircuitBreaker breaker => new CircuitBreakerRestriction(breaker.Width, breaker.HaltFor),
                _ => throw new ArgumentException($"Unknown price restriction {config.GetType().Name}")
            }).ToList();

    // Null when every entry-scoped restriction allows the price, otherwise the rejection the first
    // refusing one asks for.
    public OrderRejectedReason? RefusesEntry(long priceTicks, DateTime time)
    {
        foreach (var restriction in _restrictions)
        {
            if (restriction.Scope.HasFlag(RestrictionScope.OrderEntry) &&
                !restriction.Allows(priceTicks, time))
                return restriction.EntryRejectionReason;
        }

        return null;
    }

    // A stop elected far from its trigger would rest at a price the band would never have accepted
    // directly, so CME bounds the gap by the same band. Checked on the pair rather than on either
    // price, and only where a band exists to bound it.
    public bool AllowsStopSpread(long triggerTicks, long priceTicks)
    {
        var spread = Math.Abs(priceTicks - triggerTicks);

        foreach (var restriction in _restrictions)
        {
            if (restriction.Scope.HasFlag(RestrictionScope.OrderEntry) &&
                !restriction.AllowsStopSpread(spread))
                return false;
        }

        return true;
    }

    // The severest consequence among the trade-scoped restrictions that disallow priceTicks; a
    // pure query, consulted by Matcher.Run only outside an auction uncrossing pass.
    public RestrictionBreach? WorstTradeBreach(long priceTicks, DateTime time)
    {
        RestrictionBreach? worst = null;

        foreach (var restriction in _restrictions)
        {
            if (!restriction.Scope.HasFlag(RestrictionScope.Trade) || restriction.Allows(priceTicks, time))
                continue;

            var breach = new RestrictionBreach(restriction.OnBreach, restriction.ResumeAfter);
            if (worst == null || IsMoreSevere(breach, worst.Value))
                worst = breach;
        }

        return worst;
    }

    // Whether anything refuses to let an interruption end at the price it would end at. Eurex
    // extends a volatility interruption rather than resolving it at a price still too far out;
    // without a restriction configured for that, this always declines to interfere.
    //
    // Whether there is a price to ask about at all is the book's question, not this one's - see
    // OrderBook.CheckResumptionRefusal, which decides that and then asks this.
    public RestrictionBreach? RefusesResumption(long priceTicks, DateTime time)
    {
        foreach (var restriction in _restrictions)
        {
            if (restriction.Scope.HasFlag(RestrictionScope.Trade) &&
                !restriction.AllowsResumption(priceTicks, time))
                return new RestrictionBreach(restriction.OnBreach, restriction.ResumeAfter);
        }

        return null;
    }

    // The three things a restriction is told rather than asked. Every restriction hears all of
    // them whatever its scope, because what a restriction remembers is its own business - a
    // trade-scoped range tracks the prints it will later band against, and an entry-scoped band
    // anchors on the same ones.
    public void OnIndicativePrice(long? priceTicks)
    {
        foreach (var restriction in _restrictions)
            restriction.OnIndicativePrice(priceTicks);
    }

    public void OnTrade(long priceTicks, DateTime time)
    {
        foreach (var restriction in _restrictions)
            restriction.OnTrade(priceTicks, time);
    }

    public void OnSessionChange(long? referencePriceTicks)
    {
        foreach (var restriction in _restrictions)
            restriction.OnSessionChange(referencePriceTicks);
    }

    // Consequence first, then how long it lasts - a price through a circuit breaker's widest level
    // is through its narrower ones too, and the market should be halted for as long as the level it
    // actually reached says rather than the one it passed on the way. Never resuming outranks any
    // duration, which is what the level that ends a trading day is.
    //
    // Internal rather than private so the ranking can be tested as the table it is, without a book
    // to drive or a pair of restrictions to arrange into disagreement.
    internal static bool IsMoreSevere(RestrictionBreach candidate, RestrictionBreach current)
    {
        if (Severity(candidate.Action) != Severity(current.Action))
            return Severity(candidate.Action) > Severity(current.Action);

        if (!candidate.ResumeAfter.HasValue || !current.ResumeAfter.HasValue)
            return !candidate.ResumeAfter.HasValue && current.ResumeAfter.HasValue;

        return candidate.ResumeAfter.Value > current.ResumeAfter.Value;
    }

    // Ranked explicitly rather than leaning on the enum's declaration order, which is free to
    // change. Reject never reaches here - it is an order-entry consequence.
    private static int Severity(RestrictionBreachAction action) => action switch
    {
        RestrictionBreachAction.Halt => 3,
        RestrictionBreachAction.Pause => 2,

        // Below both: a limit-locked market is still open and still trading, at the limit. It is
        // the mildest thing that can stop a sweep, not a form of interruption.
        RestrictionBreachAction.Block => 1,
        _ => 0
    };
}
