using System;

namespace Circus.OrderBook
{
    // Same shape as OrderPriceRestriction, but a separate, independently-configurable (typically
    // narrower) band checked against the prospective trade price, not the submitted order price -
    // a breach here doesn't reject the order, it pauses continuous trading into an auction instead
    // (Eurex-style volatility interruption). OrderPriceRestriction still applies as the hard outer
    // limit checked at order entry, so this only ever matters for prices that already passed that
    // check. Named for what it is - a same-day volatility-interruption band - distinct from a
    // future circuit breaker's larger, market-wide halt. Keeps its own anchor.
    internal sealed class DailyPriceBandLimit : IPriceRestriction
    {
        private readonly int? _bandTicks;
        private long? _referencePriceTicks;

        // The band width only - see OrderPriceRestriction for why the Security itself stays out.
        internal DailyPriceBandLimit(int? bandTicks)
        {
            _bandTicks = bandTicks;
        }

        public RestrictionScope Scope => RestrictionScope.Trade;
        public RestrictionBreachAction OnBreach => RestrictionBreachAction.Pause;

        public bool Allows(long priceTicks) =>
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
