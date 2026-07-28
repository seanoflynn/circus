using System.Collections.Generic;

namespace Circus.OrderBook
{
    // What Matcher.Run decided should happen next, without having mutated anything or emitted any
    // events itself - InMemoryOrderBook.Apply is what actually does that.
    internal abstract record MatchOutcome;

    internal sealed record SelfMatchDetected(InternalOrder Resting, InternalOrder Aggressor,
        SelfMatchPreventionInstruction Instruction) : MatchOutcome;

    // UsesFullRemainingQuantity mirrors the IMatchingAlgorithm that decided this trade (see
    // IMatchingAlgorithm.cs) - it tells Apply which InternalOrder fill method to use.
    internal sealed record TradeExecuted(InternalOrder Resting, InternalOrder Aggressor, long PriceTicks,
        int Quantity, bool UsesFullRemainingQuantity) : MatchOutcome;

    // Terminal for the Run this came from - Matcher stops yielding after this, matching Match()'s
    // previous early-return on a trade-restriction breach.
    internal sealed record TradeRestrictionBreached(long PriceTicks, RestrictionBreachAction Action) : MatchOutcome;

    // Orders pulled from Stops by the trade that just printed, in trigger order (FIFO by
    // SequenceNumber across both sides) - still resting in Stops until Apply removes them.
    internal sealed record StopsTriggered(IReadOnlyList<InternalOrder> Orders) : MatchOutcome;
}
