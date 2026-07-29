namespace Circus.OrderBook;

// Flags, because a daily price limit is both: it refuses an order priced beyond the limit and
// refuses to print through it. Everything else governs one or the other.
[Flags]
internal enum RestrictionScope
{
    OrderEntry = 1,
    Trade = 2
}

internal enum RestrictionBreachAction
{
    Reject,
    Block,
    Pause,
    Halt
}

// A breached Trade-scoped restriction: what it costs the book, and how long for. ResumeAfter
// null leaves the interruption open-ended, waiting for someone to end it explicitly - and for
// Block means nothing at all, since a limit does not interrupt anything to be resumed from.
internal readonly record struct RestrictionBreach(RestrictionBreachAction Action, TimeSpan? ResumeAfter);

// A price-restriction adapter. Each owns its own reference price - there is no shared anchor -
// updating it from OnTrade (ranges that follow the market), OnIndicativePrice (the entry band
// during an auction) and/or OnSessionChange (limits fixed against a settlement price).
internal interface IPriceRestriction
{
    RestrictionScope Scope { get; }

    // Only consulted for Scope carrying Trade - an OrderEntry breach always rejects the order.
    RestrictionBreachAction OnBreach { get; }

    // Which rejection an order refused by this restriction earns. Only consulted for Scope
    // carrying OrderEntry: an order turned away by a band and one turned away by a daily limit
    // are not the same thing to whoever sent it.
    OrderRejectedReason EntryRejectionReason { get; }

    // How long the interruption this restriction causes lasts before the book returns by
    // itself. Null means it does not end on its own. Only consulted for Scope == Trade.
    TimeSpan? ResumeAfter { get; }

    // The time is the moment being judged, not just bookkeeping: a restriction measuring
    // movement over a rolling window has to know how much of its history is still in scope.
    bool Allows(long priceTicks, DateTime time);

    // How far apart a stop order's trigger and limit prices may be. Separate from Allows
    // because it measures a width rather than a distance from a reference, and CME governs it
    // with the same band that governs entry prices. Only consulted for Scope == OrderEntry.
    bool AllowsStopSpread(long spreadTicks);

    // Whether an interruption may end with a print at this price. Eurex holds the closing price
    // of a volatility interruption to a wider range than the one that caused it, and extends
    // the interruption rather than resolving it outside that range. A restriction with no such
    // range says yes and lets the interruption end. Only consulted for Scope == Trade.
    bool AllowsResumption(long priceTicks, DateTime time);

    // Maintain this restriction's own anchor. OnTrade fires on every print; OnSessionChange
    // fires when an explicit reference price (settlement-style) is supplied at a status change;
    // OnIndicativePrice fires when the auction quote moves, with null when it is withdrawn.
    void OnTrade(long priceTicks, DateTime time);
    void OnSessionChange(long? referencePriceTicks);
    void OnIndicativePrice(long? priceTicks);
}
