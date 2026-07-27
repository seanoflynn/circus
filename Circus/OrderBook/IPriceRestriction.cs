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

    // A price-restriction adapter: the order-entry price band and the trade-time volatility band
    // are the two that exist today; velocity limits and circuit breakers are expected future
    // adapters. Each adapter owns its own reference price - there is no shared anchor - updating
    // it from OnTrade (last-trade-anchored bands) and/or OnSessionChange (session-fixed limits).
    internal interface IPriceRestriction
    {
        RestrictionScope Scope { get; }

        // Only consulted for Scope == Trade - an OrderEntry breach always means "reject the order".
        RestrictionBreachAction OnBreach { get; }

        bool Allows(long priceTicks);

        // Maintain this restriction's own anchor. OnTrade fires on every print; OnSessionChange
        // fires when an explicit reference price (settlement-style) is supplied at a status change.
        void OnTrade(long priceTicks, DateTime time);
        void OnSessionChange(long? referencePriceTicks);
    }
}
