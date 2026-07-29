using System;

namespace Circus.OrderBook
{
    internal enum RestrictionScope
    {
        OrderEntry,
        Trade
    }

    internal enum RestrictionBreachAction
    {
        Reject,
        Pause,
        Halt
    }

    // A breached Trade-scoped restriction: what it costs the book, and how long for. ResumeAfter
    // null leaves the interruption open-ended, waiting for someone to end it explicitly.
    internal readonly record struct RestrictionBreach(RestrictionBreachAction Action, TimeSpan? ResumeAfter);

    // A price-restriction adapter: the order-entry price band and the trade-time volatility band
    // are the two that exist today; velocity limits and circuit breakers are expected future
    // adapters. Each adapter owns its own reference price - there is no shared anchor - updating
    // it from OnTrade (last-trade-anchored bands) and/or OnSessionChange (session-fixed limits).
    internal interface IPriceRestriction
    {
        RestrictionScope Scope { get; }

        // Only consulted for Scope == Trade - an OrderEntry breach always means "reject the order".
        RestrictionBreachAction OnBreach { get; }

        // How long the interruption this restriction causes lasts before the book returns by
        // itself. Null means it does not end on its own. Only consulted for Scope == Trade.
        TimeSpan? ResumeAfter { get; }

        // The time is the moment being judged, not just bookkeeping: a restriction measuring
        // movement over a rolling window has to know how much of its history is still in scope.
        bool Allows(long priceTicks, DateTime time);

        // Maintain this restriction's own anchor. OnTrade fires on every print; OnSessionChange
        // fires when an explicit reference price (settlement-style) is supplied at a status change.
        void OnTrade(long priceTicks, DateTime time);
        void OnSessionChange(long? referencePriceTicks);
    }
}
