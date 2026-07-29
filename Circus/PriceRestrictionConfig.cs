using System;

namespace Circus
{
    // What restrictions a security trades under, as data. The book turns each of these into the
    // adapter that enforces it, so adding a restriction is adding a case here rather than another
    // optional parameter on Security - which is what these replaced.
    //
    // A restriction that does not apply is left out of the list rather than configured with no
    // width: absence is modelled by absence.
    public abstract record PriceRestrictionConfig;

    // Rejects an order priced too far from the reference, at entry. CME's price banding.
    public sealed record OrderPriceBand(int BandTicks) : PriceRestrictionConfig;

    // Interrupts trading when a prospective trade price falls too far from the reference, rather
    // than rejecting the order that would have caused it. Eurex's volatility interruption.
    // PauseFor null leaves the interruption standing until something ends it explicitly.
    public sealed record VolatilityBand(int BandTicks, TimeSpan? PauseFor = null) : PriceRestrictionConfig;
}
