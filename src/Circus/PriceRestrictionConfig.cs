namespace Circus;

// What restrictions a security trades under, as data. The book turns each of these into the
// adapter that enforces it, so adding a restriction is adding a case here rather than another
// optional parameter on Security - which is what these replaced.
//
// A restriction that does not apply is left out of the list rather than configured with no
// width: absence is modelled by absence.
//
// Declarations only, and deliberately alongside Security rather than with the adapters in
// OrderBook/Restrictions: this is part of describing an instrument, so a caller building a
// Security should not have to reach into the book to say what it trades under. Each adapter is
// named for the config it enforces - VolatilityBand is enforced by VolatilityBandRestriction.
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

// "Too far, too fast": the same windowed range a dynamic volatility interruption uses, at the
// short timescale that catches a run of steps each unremarkable next to the last. CME's
// velocity logic, which it describes as watching what price banding cannot - banding catches a
// price that goes too far, this catches one that gets there too quickly.
//
// Not a different mechanism from VolatilityBand and deliberately not a different adapter: the
// window is the whole of what separates them. It is a config of its own so that a product says
// which it means, rather than leaving a reader to infer it from how short the window is.
//
// Window is required, unlike VolatilityBand's - a velocity limit without one would just be a
// range around the last trade, which is the other config.
public sealed record VelocityLimit(int RangeTicks, TimeSpan Window, TimeSpan? PauseFor = null)
    : PriceRestrictionConfig;

// How wide a limit is. Every restriction above measures in ticks because it tracks a market
// that has already moved; the ones below are set against a settlement price before the day
// starts, and CME states those as percentages - 7, 13 and 20 percent for equity index. Resolved
// to ticks the moment a reference exists, since a percentage of nothing is not a width.
public abstract record PriceLimitWidth
{
    public sealed record Ticks(int Count) : PriceLimitWidth;

    public sealed record Percent(decimal Value) : PriceLimitWidth;
}

// A session-long ceiling and floor. Trading continues at the limit price but nothing prints
// through it and no order may be entered beyond it - CME's limit-lock, which is not a halt: the
// market stays open, quotes, and can trade back inside.
public sealed record DailyPriceLimit(PriceLimitWidth Width) : PriceRestrictionConfig;

// A threshold that stops trading outright rather than capping it. Several are ordinarily
// configured together - CME's equity index breakers halt at 7 and 13 percent and end the day at
// 20 - and the widest one breached is the one served, so a price through all three halts for
// however long the 20 percent level says rather than the 7 percent level.
//
// HaltFor null never resumes on its own, which is what the level that ends a trading day wants:
// whoever drives the book closes it.
public sealed record CircuitBreaker(PriceLimitWidth Width, TimeSpan? HaltFor = null) : PriceRestrictionConfig;
