using System.Collections.Generic;

namespace Circus.OrderBook
{
    // Which resting order an aggressor trades against next, for how much, and at what price.
    internal readonly record struct Allocation(InternalOrder Resting, int Quantity, long PriceTicks);

    // How the next trade is allocated and priced. Selected per phase by InMemoryOrderBook and
    // consulted by Matcher.Run; the loop itself - the crossing condition, which side is the
    // aggressor, self-match detection, stop-triggering - is identical whichever algorithm is
    // active and stays owned by Matcher. Only the decisions below vary.
    //
    // Implementations are instances rather than shared singletons, so a security can eventually
    // carry its own configured set - the auction governing pre-open and the opening print, and
    // price-time (or a pro-rata variant of it) governing continuous trading.
    internal interface IMatchingAlgorithm
    {
        // What this algorithm would print against the current book if it ran right now, without
        // committing to any of it. An auction answers with its clearing price and the volume that
        // would cross there, which is what pre-open publishes as an indicative quote; continuous
        // trading has no single indicative print to give - the touch is already public - and
        // declines. Side-effect-free, so asking is not the same as beginning.
        bool TryQuoteIndicative(IReadOnlyDictionary<Side, IReadOnlyPriceLadder> working,
            out long priceTicks, out int quantity);

        // Prepare for a run against the current working book, deriving whatever run-scoped state
        // this algorithm needs from it - an auction strikes its clearing price here. False means
        // there is nothing for this algorithm to match, so the run yields no outcomes at all.
        bool TryBegin(IReadOnlyDictionary<Side, IReadOnlyPriceLadder> working);

        // Picks the counterparty for the next trade. This is the decision that actually separates
        // one matching algorithm from another - price-time takes the head of the level, pro-rata
        // divides the aggressor across the whole level in proportion to size - so it belongs here
        // rather than in the loop.
        //
        // restingHead is the FIFO-earliest order at the resting side's best level; walk LevelNext
        // for the rest of that level. The order returned must come from that level, but need not
        // be the head: PriceLadder unlinks from any position, so allocating out of FIFO order is
        // safe. Null declines to match any further and ends the run - a crossed book normally
        // must not decline, and neither algorithm here ever does.
        //
        // An algorithm caching per-level state across calls should key it on the level's price
        // tick and recompute when the level's composition changes: a selection can still be
        // dropped before it trades if self-match prevention cancels the pair, which would leave
        // pre-computed shares stale. Neither algorithm here is stateful, so nothing today relies
        // on this.
        Allocation? SelectNext(InternalOrder restingHead, InternalOrder aggressor);

        // Whether a fill allocates against an order's full remaining quantity rather than its
        // displayed peak - tells InMemoryOrderBook.Apply which InternalOrder fill method to use.
        bool UsesFullRemainingQuantity { get; }

        // Whether a prospective trade price is checked against Trade-scoped price restrictions
        // (a volatility band pausing the book, say) before it executes.
        bool ChecksTradeRestrictions { get; }

        // Anchor maintenance, mirroring IPriceRestriction.OnTrade / OnSessionChange. An algorithm
        // that keys off a reference price - the auction's tie-break does - keeps it current here
        // rather than reading one the book holds on its behalf. The book fans both across every
        // algorithm it governs a phase with, so an algorithm with no anchor simply ignores them.
        void OnTrade(long priceTicks);

        void OnSessionChange(long? referencePriceTicks);
    }
}
