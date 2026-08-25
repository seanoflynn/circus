using Circus.Events;

namespace Circus.Restrictions;

internal interface IPriceRestriction
{
    RestrictionScope Scope { get; }

    RestrictionBreachAction OnBreach { get; }

    OrderRejectedReason EntryRejectionReason { get; }

    TimeSpan? ResumeAfter { get; }

    bool Allows(long priceTicks, DateTime time);

    bool AllowsStopSpread(long spreadTicks);

    bool AllowsResumption(long priceTicks, DateTime time);

    void OnTrade(long priceTicks, DateTime time);
    void OnSessionChange(long? referencePriceTicks);
    void OnIndicativePrice(long? priceTicks);
}
