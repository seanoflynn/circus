namespace Circus.OrderBook;

// A threshold that stops trading rather than capping it. Set against a settlement price like a
// daily limit, and unlike one it halts: no matching, no quote, nothing published until it
// resumes or someone intervenes.
//
// Several are ordinarily configured together, and a price through the widest is through all of
// them. The book serves the severest breach, ranking a longer halt above a shorter one and one
// that never resumes above both - so the level that ends a trading day wins over the levels it
// passed through on the way.
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

    // Trade-scoped only, so this is never asked.
    public OrderRejectedReason EntryRejectionReason => OrderRejectedReason.BeyondDailyPriceLimit;

    // Null never resumes on its own, which is what the level that ends a trading day wants.
    public TimeSpan? ResumeAfter => _haltFor;

    public bool Allows(long priceTicks, DateTime time) => _anchor.Allows(priceTicks);

    public bool AllowsStopSpread(long spreadTicks) => true;

    // A halt is ended by its own clock or by whoever drives the book, not by where the auction
    // would print - so this never holds one open.
    public bool AllowsResumption(long priceTicks, DateTime time) => true;

    public void OnTrade(long priceTicks, DateTime time)
    {
    }

    public void OnIndicativePrice(long? priceTicks)
    {
    }

    public void OnSessionChange(long? referencePriceTicks) => _anchor.OnSessionChange(referencePriceTicks);
}
