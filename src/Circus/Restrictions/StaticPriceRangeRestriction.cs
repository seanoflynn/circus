using Circus.Events;

namespace Circus.Restrictions;

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

    public OrderRejectedReason EntryRejectionReason => OrderRejectedReason.PriceOutsideBands;

    public TimeSpan? ResumeAfter => _resumeAfter;

    public bool Allows(long priceTicks, DateTime time) =>
        !_referencePriceTicks.HasValue ||
        Math.Abs(priceTicks - _referencePriceTicks.Value) <= _rangeTicks;

    public bool AllowsResumption(long priceTicks, DateTime time) => true;

    public bool AllowsStopSpread(long spreadTicks) => true;

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
