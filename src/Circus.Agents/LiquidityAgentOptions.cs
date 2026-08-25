namespace Circus.Agents;

public record LiquidityAgentOptions(
    decimal ReferencePrice = 1000m,

    int Depth = 3,
    int LevelSpacingTicks = 1,

    int MinQuantity = 1,
    int MaxQuantity = 10,

    int MaxLiveOrders = 20,

    double ActProbability = 0.5,

    double Aggression = 0.05,

    int SweepTicks = 0,

    double MarketOrderProbability = 0.0,

    double CancelProbability = 0.1,
    double ReplaceProbability = 0.2,

    int? MaxPosition = null,

    int? MaxVisibleQuantity = null,

    OrderValidity? Validity = null,

    SelfMatchPreventionInstruction? SelfMatchPrevention = Circus.SelfMatchPreventionInstruction.CancelResting
)
{
    public void Validate()
    {
        if (Depth < 1) throw new ArgumentException("Depth must be at least 1", nameof(Depth));
        if (LevelSpacingTicks < 1)
            throw new ArgumentException("LevelSpacingTicks must be at least 1", nameof(LevelSpacingTicks));
        if (MinQuantity < 1) throw new ArgumentException("MinQuantity must be at least 1", nameof(MinQuantity));
        if (MaxQuantity < MinQuantity)
            throw new ArgumentException("MaxQuantity must be at least MinQuantity", nameof(MaxQuantity));
        if (MaxLiveOrders < 1)
            throw new ArgumentException("MaxLiveOrders must be at least 1", nameof(MaxLiveOrders));
        if (SweepTicks < 0) throw new ArgumentException("SweepTicks cannot be negative", nameof(SweepTicks));
        if (MaxPosition is < 0) throw new ArgumentException("MaxPosition cannot be negative", nameof(MaxPosition));
        if (MaxVisibleQuantity is < 1)
            throw new ArgumentException("MaxVisibleQuantity must be at least 1", nameof(MaxVisibleQuantity));

        Probability(ActProbability, nameof(ActProbability));
        Probability(Aggression, nameof(Aggression));
        Probability(MarketOrderProbability, nameof(MarketOrderProbability));
        Probability(CancelProbability, nameof(CancelProbability));
        Probability(ReplaceProbability, nameof(ReplaceProbability));
    }

    private static void Probability(double value, string name)
    {
        if (value is < 0 or > 1 || double.IsNaN(value))
            throw new ArgumentException($"{name} must be a probability between 0 and 1", name);
    }
}
