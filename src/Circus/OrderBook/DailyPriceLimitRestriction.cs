namespace Circus.OrderBook;

// A session-long ceiling and floor set against a settlement price. The only restriction that
// neither rejects nor interrupts: trading continues, at the limit price, and simply cannot go
// through it. CME's limit-lock, which is why a limit-up market is still open and still quoting
// rather than halted - it can trade back inside at any moment.
//
// Both scopes, so one anchor answers both questions: an order priced beyond the limit is turned
// away at entry, and a prospective trade beyond it does not print. Those have to agree, and the
// surest way to make them agree is for them to be the same object.
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

    // A limit interrupts nothing, so there is nothing to resume from. It ends when the session
    // does, or when a new reference price replaces it.
    public TimeSpan? ResumeAfter => null;

    public bool Allows(long priceTicks, DateTime time) => _anchor.Allows(priceTicks);

    // Not a band: it has no view on how a stop is priced beyond the limit check itself, which
    // the trigger and limit prices each face on their own.
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
