namespace Circus.OrderBook
{
    // How to price and size the next trade. Selected per phase by InMemoryOrderBook and consulted
    // by Matcher.Run; the loop itself - the crossing condition, self-match detection,
    // stop-triggering - is identical whichever algorithm is active and stays owned by Matcher.
    // Only the decisions below vary.
    //
    // Implementations are instances rather than shared singletons, so a security can eventually
    // carry its own configured set: a dry-run auction publishing an indicative price during
    // pre-open, a committing auction for the opening print, price-time (or a pro-rata variant of
    // it) once continuous trading starts.
    internal interface IMatchingAlgorithm
    {
        // The price a trade against this resting order should print at.
        long PriceTicks(InternalOrder resting);

        // How much of that trade should execute.
        int Quantity(InternalOrder resting, InternalOrder aggressor);

        // Whether a fill allocates against an order's full remaining quantity rather than its
        // displayed peak - tells InMemoryOrderBook.Apply which InternalOrder fill method to use.
        bool UsesFullRemainingQuantity { get; }

        // Whether a prospective trade price is checked against Trade-scoped price restrictions
        // (a volatility band pausing the book, say) before it executes.
        bool ChecksTradeRestrictions { get; }
    }
}
