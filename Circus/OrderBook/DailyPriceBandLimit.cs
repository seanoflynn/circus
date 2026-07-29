using System;

namespace Circus.OrderBook
{
    // A separate, typically narrower band checked against the prospective trade price rather than
    // the submitted one. A breach pauses continuous trading into an auction rather than rejecting,
    // Eurex-style. Named for what it is - a same-day volatility interruption - as distinct from a
    // future circuit breaker's market-wide halt.
    internal sealed class DailyPriceBandLimit : IPriceRestriction
    {
        private readonly int? _bandTicks;
        private readonly TimeSpan? _resumeAfter;
        private long? _referencePriceTicks;

        internal DailyPriceBandLimit(int? bandTicks, TimeSpan? resumeAfter = null)
        {
            _bandTicks = bandTicks;
            _resumeAfter = resumeAfter;
        }

        public RestrictionScope Scope => RestrictionScope.Trade;
        public RestrictionBreachAction OnBreach => RestrictionBreachAction.Pause;

        // Eurex times its volatility interruptions rather than leaving them open; configuring no
        // duration leaves the pause standing until someone ends it, which is the older behaviour.
        public TimeSpan? ResumeAfter => _resumeAfter;

        // Anchored on the last trade rather than a window, so the time is not consulted.
        public bool Allows(long priceTicks, DateTime time) =>
            !_bandTicks.HasValue || !_referencePriceTicks.HasValue ||
            Math.Abs(priceTicks - _referencePriceTicks.Value) <= _bandTicks.Value;

        public void OnTrade(long priceTicks, DateTime time) => _referencePriceTicks = priceTicks;

        public void OnSessionChange(long? referencePriceTicks)
        {
            if (referencePriceTicks.HasValue)
                _referencePriceTicks = referencePriceTicks;
        }
    }
}
