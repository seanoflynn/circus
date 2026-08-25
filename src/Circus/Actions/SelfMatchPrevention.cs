namespace Circus.Actions;

public sealed record SelfMatchPrevention
{
    public required string Id { get; init; }
    public SelfMatchPreventionInstruction? Instruction { get; init; }
}
