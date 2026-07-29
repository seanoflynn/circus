namespace Circus.OrderBook;

// What a phase permits and which algorithm governs it. Everything the book does differently
// between phases lives here rather than in comparisons against OrderBookStatus, so adding a
// phase is adding a row.
internal sealed record TradingPhase(
    IMatchingAlgorithm? Algorithm,
    bool AcceptsOrderActions,
    bool AcceptsMarketOrders,
    bool MatchesContinuously,
    bool StartsSession,
    bool ExpiresDayOrders)
{
    // A phase that quotes without trading is accumulating orders for a print, so leaving it is
    // where that print belongs - the opening print is pre-open's auction, not the open phase's.
    public bool PrintsOnExit => Algorithm != null && !MatchesContinuously;
}
