using Circus.Events;

namespace Circus.Restrictions;

internal sealed class VolatilityBandRestriction : IPriceRestriction
{
    private readonly int _rangeTicks;
    private readonly TimeSpan? _window;
    private readonly TimeSpan? _resumeAfter;
    private readonly int? _extendedRangeTicks;

    private readonly Queue<(long PriceTicks, DateTime Time)> _recentTrades = new();

    private long? _sessionPriceTicks;

    internal VolatilityBandRestriction(int rangeTicks, TimeSpan? resumeAfter = null,
        TimeSpan? window = null, int? extendedRangeTicks = null)
    {
        _rangeTicks = rangeTicks;
        _resumeAfter = resumeAfter;
        _window = window;
        _extendedRangeTicks = extendedRangeTicks;
    }

    public RestrictionScope Scope => RestrictionScope.Trade;
    public RestrictionBreachAction OnBreach => RestrictionBreachAction.Pause;

    public OrderRejectedReason EntryRejectionReason => OrderRejectedReason.PriceOutsideBands;

    public TimeSpan? ResumeAfter => _resumeAfter;

    public bool Allows(long priceTicks, DateTime time) => Within(priceTicks, time, _rangeTicks);

    public bool AllowsResumption(long priceTicks, DateTime time) =>
        !_extendedRangeTicks.HasValue || Within(priceTicks, time, _extendedRangeTicks.Value);

    public bool AllowsStopSpread(long spreadTicks) => true;

    public void OnTrade(long priceTicks, DateTime time)
    {
        if (!_window.HasValue)
            _recentTrades.Clear();

        _recentTrades.Enqueue((priceTicks, time));
    }

    public void OnIndicativePrice(long? priceTicks)
    {
    }

    public void OnSessionChange(long? referencePriceTicks)
    {
        if (!referencePriceTicks.HasValue)
            return;

        _sessionPriceTicks = referencePriceTicks;
        _recentTrades.Clear();
    }

    private bool Within(long priceTicks, DateTime time, int rangeTicks)
    {
        Evict(time);

        if (_recentTrades.Count == 0)
            return !_sessionPriceTicks.HasValue ||
                   Math.Abs(priceTicks - _sessionPriceTicks.Value) <= rangeTicks;

        foreach (var (tradePriceTicks, _) in _recentTrades)
        {
            if (Math.Abs(priceTicks - tradePriceTicks) > rangeTicks)
                return false;
        }

        return true;
    }

    // Never empties the queue when a window is configured: the newest trade is kept whatever its
    // age, so a market gone quiet is still measured against where it last traded.
    private void Evict(DateTime time)
    {
        if (!_window.HasValue)
            return;

        var cutoff = time - _window.Value;
        while (_recentTrades.Count > 1 && _recentTrades.Peek().Time < cutoff)
            _recentTrades.Dequeue();
    }
}
