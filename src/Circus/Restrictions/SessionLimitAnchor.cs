namespace Circus.Restrictions;

internal sealed class SessionLimitAnchor
{
    private readonly PriceLimitWidth _width;

    private long? _referencePriceTicks;
    private long _widthTicks;

    internal SessionLimitAnchor(PriceLimitWidth width)
    {
        _width = width;
    }

    internal bool Allows(long priceTicks) =>
        !_referencePriceTicks.HasValue || Math.Abs(priceTicks - _referencePriceTicks.Value) <= _widthTicks;

    internal long WidthTicks => _referencePriceTicks.HasValue ? _widthTicks : 0;

    internal void OnSessionChange(long? referencePriceTicks)
    {
        if (!referencePriceTicks.HasValue)
            return;

        _referencePriceTicks = referencePriceTicks;
        _widthTicks = Resolve(referencePriceTicks.Value);
    }

    private long Resolve(long referencePriceTicks) => _width switch
    {
        PriceLimitWidth.Ticks ticks => ticks.Count,
        PriceLimitWidth.Percent percent =>
            (long) Math.Round(Math.Abs(referencePriceTicks) * percent.Value / 100m, MidpointRounding.AwayFromZero),
        _ => throw new ArgumentException($"Unknown price limit width {_width.GetType().Name}")
    };
}
