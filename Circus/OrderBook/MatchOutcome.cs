using System.Collections.Generic;

namespace Circus.OrderBook
{
    // What Matcher.Run decided should happen next. Run mutates nothing and emits nothing;
    // InMemoryOrderBook.Apply does both.
    internal abstract record MatchOutcome;

    internal sealed record SelfMatchDetected(InternalOrder Resting, InternalOrder Aggressor,
        SelfMatchPreventionInstruction Instruction) : MatchOutcome;

    internal sealed record TradeExecuted(InternalOrder Resting, InternalOrder Aggressor, long PriceTicks,
        int Quantity, bool UsesFullRemainingQuantity) : MatchOutcome;

    // Terminal: Run stops yielding after this.
    internal sealed record TradeRestrictionBreached(long PriceTicks, RestrictionBreachAction Action) : MatchOutcome;

    // Elected by the trade that just printed, in FIFO order across both sides, and still resting
    // in the stop ladders until Apply removes them.
    internal sealed record StopsTriggered(IReadOnlyList<InternalOrder> Orders) : MatchOutcome;
}
