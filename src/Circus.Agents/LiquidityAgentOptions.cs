namespace Circus.Agents;

// How a LiquidityAgent behaves. Every field is a dial rather than a switch, so a venue can be
// populated with agents that differ only in their numbers - a patient one quoting deep and wide,
// an impatient one churning at the touch - without any of them being a different type.
//
// The probabilities are independent draws, not weights that share a budget: a tick can cancel,
// reprice, cross and quote, or do none of those. Weights carving up a single roll would make the
// behaviours alternatives to each other, which they are not - an agent that repriced a quote has
// not thereby decided against adding one.
public record LiquidityAgentOptions(
    // Where to quote around before the market has said anything - no mid, no last trade. Aligned
    // to the instrument's tick before use.
    decimal ReferencePrice = 1000m,

    // Levels quoted each side. Rung 0 sits one spacing off the reference, so the agent's own bid
    // and offer never meet however deep it quotes.
    int Depth = 3,
    int LevelSpacingTicks = 1,

    int MinQuantity = 1,
    int MaxQuantity = 10,

    // A cap across every instrument the agent trades, checked before each order is written. It
    // bounds what the agent is holding, not what it is worth.
    int MaxLiveOrders = 20,

    // Chance of doing anything at all in an instrument on a given tick. The rest of the
    // probabilities are drawn only once this one has passed, so halving this halves everything.
    double ActProbability = 0.5,

    // Chance of crossing the spread rather than adding to it. The one dial that turns a pure
    // liquidity provider into something that trades: at 0 the agent never takes.
    double Aggression = 0.05,

    // How far through the opposite touch a crossing order is priced. 0 takes the touch and
    // stops there; higher clears several levels in one print.
    int SweepTicks = 0,

    // Chance that a crossing order is a market order rather than a marketable limit. Drawn only
    // when the agent has already decided to cross, because a market order is aggression - and
    // only sent when the other side is quoting, since a market order with nothing to match is
    // refused rather than rested.
    double MarketOrderProbability = 0.0,

    // Chance of retiring one live order, and of moving one to a fresh rung. Drawn separately, so
    // a tick can do both - to different orders, never the same one twice.
    double CancelProbability = 0.1,
    double ReplaceProbability = 0.2,

    // Stops the agent adding to whichever side would grow its position past this. It bounds what
    // the agent will send, not what it can end up holding: orders already resting go on filling,
    // so a position can overshoot and then be worked back rather than being clamped outright.
    int? MaxPosition = null,

    // Set to show only part of each order to the market. Clamped to the order's own quantity,
    // since a peak larger than the order is refused.
    int? MaxVisibleQuantity = null,

    // Defaults to GoodTilCanceled when left null, so a run spanning a close keeps its book.
    OrderValidity? Validity = null,

    // Carried on every order, under the agent's company id, so an aggressive order that would
    // trade against the agent's own resting one is prevented rather than washed. Null turns it
    // off, and a lone agent in a venue will then trade with itself.
    //
    // Qualified because the parameter shares its name with a type, which is one of the places
    // a default value reads as ambiguous.
    SelfMatchPreventionInstruction? SelfMatchPrevention = Circus.SelfMatchPreventionInstruction.CancelResting
)
{
    // Checked once, where the numbers arrive, rather than by each thing that reads them. Every
    // one of these produces flow the venue would refuse or an agent that silently does nothing,
    // and both are worse to debug than an exception naming the field.
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
