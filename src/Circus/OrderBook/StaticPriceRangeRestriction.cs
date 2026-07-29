namespace Circus.OrderBook;

// A range measured from a fixed reference rather than from recent trades. Eurex's static
// volatility interruption, which exists because a range that follows the market can be walked
// anywhere over a day without ever being breached - every step is small next to the last one.
//
// Deliberately deaf to trades. That is the whole difference between this and the dynamic range,
// and it is why the two are separate adapters rather than one with a mode: each owns its anchor.
internal sealed class StaticPriceRangeRestriction : IPriceRestriction
{
    private readonly int _rangeTicks;
    private readonly TimeSpan? _resumeAfter;
    private long? _referencePriceTicks;

    internal StaticPriceRangeRestriction(int rangeTicks, TimeSpan? resumeAfter = null)
    {
        _rangeTicks = rangeTicks;
        _resumeAfter = resumeAfter;
    }

    public RestrictionScope Scope => RestrictionScope.Trade;
    public RestrictionBreachAction OnBreach => RestrictionBreachAction.Pause;

    // Trade-scoped only, so this is never asked.
    public OrderRejectedReason EntryRejectionReason => OrderRejectedReason.PriceOutsideBands;

    public TimeSpan? ResumeAfter => _resumeAfter;

    // Inactive until a reference exists - nothing but a status change can supply one.
    public bool Allows(long priceTicks, DateTime time) =>
        !_referencePriceTicks.HasValue ||
        Math.Abs(priceTicks - _referencePriceTicks.Value) <= _rangeTicks;

    // No wider range of its own: a static range is already the outer bound, so it never has an
    // opinion about whether an interruption should keep running.
    public bool AllowsResumption(long priceTicks, DateTime time) => true;

    public bool AllowsStopSpread(long spreadTicks) => true;

    // Static: where the market has traded since is exactly what this is measuring away from.
    public void OnTrade(long priceTicks, DateTime time)
    {
    }

    public void OnIndicativePrice(long? priceTicks)
    {
    }

    public void OnSessionChange(long? referencePriceTicks)
    {
        if (referencePriceTicks.HasValue)
            _referencePriceTicks = referencePriceTicks;
    }
}
