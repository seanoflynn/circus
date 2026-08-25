using Circus.Matching;

namespace Circus;

internal sealed record TradingPhase(
    IMatchingAlgorithm? Algorithm,
    bool AcceptsOrderActions,
    bool AcceptsMarketOrders,
    bool MatchesContinuously,
    bool StartsSession,
    bool ExpiresDayOrders)
{
    public bool PrintsOnExit => Algorithm != null && !MatchesContinuously;
}
