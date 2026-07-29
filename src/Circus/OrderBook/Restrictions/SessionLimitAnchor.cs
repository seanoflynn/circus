namespace Circus.OrderBook.Restrictions;

// The reference and resolved width shared by the two restrictions set against a settlement
// price rather than against a moving market. Both are inert until a reference arrives, because
// a percentage width has no size until there is something to take a percentage of.
//
// Deaf to trades by construction: a limit that followed the market would never be reached.
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

    // How far beyond the limit this width reaches, for ranking one against another. Zero until
    // a reference resolves it.
    internal long WidthTicks => _referencePriceTicks.HasValue ? _widthTicks : 0;

    internal void OnSessionChange(long? referencePriceTicks)
    {
        if (!referencePriceTicks.HasValue)
            return;

        _referencePriceTicks = referencePriceTicks;
        _widthTicks = Resolve(referencePriceTicks.Value);
    }

    // A percentage of the reference in ticks is a percentage of it in price, so the tick size
    // never enters into it. Rounded to the nearest tick - a limit lands on a tradable price.
    private long Resolve(long referencePriceTicks) => _width switch
    {
        PriceLimitWidth.Ticks ticks => ticks.Count,
        PriceLimitWidth.Percent percent =>
            (long) Math.Round(Math.Abs(referencePriceTicks) * percent.Value / 100m, MidpointRounding.AwayFromZero),
        _ => throw new ArgumentException($"Unknown price limit width {_width.GetType().Name}")
    };
}
