using Circus.Events;

namespace Circus.Restrictions;

internal sealed class DailyPriceLimitRestriction : IPriceRestriction
{
    private readonly SessionLimitAnchor _anchor;

    internal DailyPriceLimitRestriction(PriceLimitWidth width)
    {
        _anchor = new SessionLimitAnchor(width);
    }

    public RestrictionScope Scope => RestrictionScope.OrderEntry | RestrictionScope.Trade;

    public RestrictionBreachAction OnBreach => RestrictionBreachAction.Block;

    public OrderRejectedReason EntryRejectionReason => OrderRejectedReason.BeyondDailyPriceLimit;

    public TimeSpan? ResumeAfter => null;

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
