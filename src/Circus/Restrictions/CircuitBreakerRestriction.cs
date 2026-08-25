using Circus.Events;

namespace Circus.Restrictions;

internal sealed class CircuitBreakerRestriction : IPriceRestriction
{
    private readonly SessionLimitAnchor _anchor;
    private readonly TimeSpan? _haltFor;

    internal CircuitBreakerRestriction(PriceLimitWidth width, TimeSpan? haltFor = null)
    {
        _anchor = new SessionLimitAnchor(width);
        _haltFor = haltFor;
    }

    public RestrictionScope Scope => RestrictionScope.Trade;

    public RestrictionBreachAction OnBreach => RestrictionBreachAction.Halt;

    public OrderRejectedReason EntryRejectionReason => OrderRejectedReason.BeyondDailyPriceLimit;

    public TimeSpan? ResumeAfter => _haltFor;

    public bool Allows(long priceTicks, DateTime time) => _anchor.Allows(priceTicks);

    public bool AllowsStopSpread(long spreadTicks) => true;

    public bool AllowsResumption(long priceTicks, DateTime time) => true;

    public void OnTrade(long priceTicks, DateTime time)
    {
    }

    public void OnIndicativePrice(long? priceTicks)
    {
    }

    public void OnSessionChange(long? referencePriceTicks) => _anchor.OnSessionChange(referencePriceTicks);
}
