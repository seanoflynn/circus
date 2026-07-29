using System;

namespace Circus.OrderBook
{
    // A band checked against the prospective trade price rather than the submitted one. A breach
    // interrupts continuous trading into an auction rather than rejecting, Eurex-style.
    //
    // Named for what it is - a volatility interruption - which leaves "daily limit" free for the
    // thing that actually is one: a session-long ceiling trading stops at rather than pauses on.
    internal sealed class VolatilityBandRestriction : IPriceRestriction
    {
        private readonly int _bandTicks;
        private readonly TimeSpan? _resumeAfter;
        private long? _referencePriceTicks;

        internal VolatilityBandRestriction(int bandTicks, TimeSpan? resumeAfter = null)
        {
            _bandTicks = bandTicks;
            _resumeAfter = resumeAfter;
        }

        public RestrictionScope Scope => RestrictionScope.Trade;
        public RestrictionBreachAction OnBreach => RestrictionBreachAction.Pause;

        // Eurex times its volatility interruptions rather than leaving them open; configuring no
        // duration leaves the pause standing until someone ends it.
        public TimeSpan? ResumeAfter => _resumeAfter;

        // Inactive until it has a reference to measure from. A band with no width is not modelled
        // here at all - a security that wants none leaves the restriction out.
        public bool Allows(long priceTicks, DateTime time) =>
            !_referencePriceTicks.HasValue ||
            Math.Abs(priceTicks - _referencePriceTicks.Value) <= _bandTicks;

        // Not an entry restriction, so it has no say in how a stop is priced.
        public bool AllowsStopSpread(long spreadTicks) => true;

        public void OnTrade(long priceTicks, DateTime time) => _referencePriceTicks = priceTicks;

        // Ignored: CME and Eurex both measure volatility against prices that actually traded, not
        // against one an auction is only quoting. The entry band does follow it - each restriction
        // owning its own anchor is what lets the two disagree.
        public void OnIndicativePrice(long? priceTicks)
        {
        }

        public void OnSessionChange(long? referencePriceTicks)
        {
            if (referencePriceTicks.HasValue)
                _referencePriceTicks = referencePriceTicks;
        }
    }
}
