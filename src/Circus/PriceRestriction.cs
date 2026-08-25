namespace Circus;

public abstract record PriceRestriction;

public sealed record OrderPriceBand(int BandTicks) : PriceRestriction;

public sealed record VolatilityBand(
    int RangeTicks,
    TimeSpan? PauseFor = null,
    TimeSpan? Window = null,
    int? ExtendedRangeTicks = null) : PriceRestriction;

public sealed record StaticPriceRange(int RangeTicks, TimeSpan? PauseFor = null) : PriceRestriction;

public sealed record VelocityLimit(int RangeTicks, TimeSpan Window, TimeSpan? PauseFor = null)
    : PriceRestriction;

public abstract record PriceLimitWidth
{
    public sealed record Ticks(int Count) : PriceLimitWidth;

    public sealed record Percent(decimal Value) : PriceLimitWidth;
}

public sealed record DailyPriceLimit(PriceLimitWidth Width) : PriceRestriction;

public sealed record CircuitBreaker(PriceLimitWidth Width, TimeSpan? HaltFor = null) : PriceRestriction;
