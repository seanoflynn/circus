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

    // Interrupts trading when a prospective trade price falls too far from where the market has
    // recently traded, rather than rejecting the order that would have caused it. Eurex's dynamic
    // volatility interruption.
    //
    // Window is the lookback: the price must be within RangeTicks of every trade inside it. Unset
    // measures against the last trade alone. ExtendedRangeTicks is the wider range Eurex checks an
    // interruption's would-be closing price against - outside it, the interruption is extended
    // rather than allowed to resolve. Unset means an interruption always ends when its time is up.
    // PauseFor null leaves the interruption standing until something ends it explicitly.
    public sealed record VolatilityBand(
        int RangeTicks,
        TimeSpan? PauseFor = null,
        TimeSpan? Window = null,
        int? ExtendedRangeTicks = null) : PriceRestrictionConfig;

    // Interrupts trading on distance from a fixed reference rather than from recent trades, so it
    // catches a whole day's drift that a range following the market never notices. Eurex's static
    // volatility interruption. Anchored only by the reference prices supplied at status changes.
    public sealed record StaticPriceRange(int RangeTicks, TimeSpan? PauseFor = null) : PriceRestrictionConfig;
}
