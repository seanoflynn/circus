using Circus.Events;

namespace Circus.Restrictions;

internal sealed class OrderPriceBandRestriction : IPriceRestriction
{
    private readonly int _bandTicks;

    private long? _indicativePriceTicks;
    private long? _lastTradePriceTicks;
    private long? _sessionPriceTicks;

    internal OrderPriceBandRestriction(int bandTicks)
    {
        _bandTicks = bandTicks;
    }

    public RestrictionScope Scope => RestrictionScope.OrderEntry;
    public RestrictionBreachAction OnBreach => RestrictionBreachAction.Reject;

    public OrderRejectedReason EntryRejectionReason => OrderRejectedReason.PriceOutsideBands;

    public TimeSpan? ResumeAfter => null;

    private long? ReferencePriceTicks =>
        _indicativePriceTicks ?? _lastTradePriceTicks ?? _sessionPriceTicks;

    public bool Allows(long priceTicks, DateTime time)
    {
        var reference = ReferencePriceTicks;
        return !reference.HasValue || Math.Abs(priceTicks - reference.Value) <= _bandTicks;
    }

    public bool AllowsStopSpread(long spreadTicks) => spreadTicks <= _bandTicks;

    public bool AllowsResumption(long priceTicks, DateTime time) => true;

    public void OnTrade(long priceTicks, DateTime time) => _lastTradePriceTicks = priceTicks;

    public void OnIndicativePrice(long? priceTicks) => _indicativePriceTicks = priceTicks;

    public void OnSessionChange(long? referencePriceTicks)
    {
        if (!referencePriceTicks.HasValue)
            return;

        _sessionPriceTicks = referencePriceTicks;
        _lastTradePriceTicks = null;
        _indicativePriceTicks = null;
    }
}
