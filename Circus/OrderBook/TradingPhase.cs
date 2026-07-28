namespace Circus.OrderBook
{
    // What a trading phase permits, and which algorithm governs it.
    //
    // Everything the book does differently from one phase to the next is a field here rather than
    // a comparison against OrderBookStatus, so the behaviour of a phase is one row to read instead
    // of guard clauses scattered across order entry, matching and the status transitions. Adding a
    // phase is adding a row; giving a security a table of its own is supplying a different set.
    internal sealed record TradingPhase(
        // Governs matching while the phase is current, and quoting throughout it. Null for a phase
        // where neither happens.
        IMatchingAlgorithm? Algorithm,
        // Whether clients can create, amend or cancel at all. A no-cancel period ahead of an
        // auction would be the reason to split this into separate facets per action; nothing needs
        // that yet, and every phase today either takes all three or none.
        bool AcceptsOrderActions,
        // Market orders need a resting book to price themselves against, so a phase that never
        // trades has nothing to protect them with.
        bool AcceptsMarketOrders,
        // Whether an incoming order is matched as it arrives. False for a phase that only
        // accumulates orders, however much its algorithm can already tell you about them.
        bool MatchesContinuously,
        // Whether entering the phase begins a new session, restarting order sequence numbers.
        bool StartsSession,
        // Whether entering the phase expires the day orders that did not survive it.
        bool ExpiresDayOrders)
    {
        // A phase that quotes a price without trading at it is accumulating orders for a print,
        // and leaving it is when that print happens - which is why the opening print is the
        // auction pre-open was quoting, rather than anything the open phase owns. Continuous
        // trading has already printed everything it is going to, and a phase with no algorithm has
        // nothing to print at all.
        public bool PrintsOnExit => Algorithm != null && !MatchesContinuously;
    }
}
