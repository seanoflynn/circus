using Circus.Restrictions;

namespace Circus.Matching;

internal abstract record MatchOutcome;

internal sealed record SelfMatchDetected(InternalOrder Resting, InternalOrder Aggressor,
    SelfMatchPreventionInstruction Instruction) : MatchOutcome;

internal sealed record TradeExecuted(InternalOrder Resting, InternalOrder Aggressor, long PriceTicks,
    int Quantity, bool UsesFullRemainingQuantity) : MatchOutcome;

internal sealed record TradeRestrictionBreached(long PriceTicks, RestrictionBreach Breach) : MatchOutcome;

internal sealed record StopsTriggered(IReadOnlyList<InternalOrder> Orders) : MatchOutcome;
