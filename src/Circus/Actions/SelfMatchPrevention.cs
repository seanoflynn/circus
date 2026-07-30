namespace Circus.Actions;

// Not an OrderBookAction: this is a qualifier carried by CreateOrder, not something a caller
// sends on its own.
//
// Id is required: an instruction with no id is meaningless (nothing to match against), so
// rather than let that combination be constructed and silently ignored, opting into self-match
// prevention at all means supplying an id - Instruction is the only genuinely optional part,
// falling back to CancelResting when omitted.
public sealed record SelfMatchPrevention
{
    public required string Id { get; init; }
    public SelfMatchPreventionInstruction? Instruction { get; init; }
}
