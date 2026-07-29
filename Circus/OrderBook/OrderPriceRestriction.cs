using System;

namespace Circus.OrderBook
{
    // The hard band checked at order entry, anchored on the last trade and seeded from an explicit
    // reference price pre-open. Sees only client-supplied resting limit prices - trigger and
    // Market/MarketLimit prices are governed elsewhere in InMemoryOrderBook.
    internal sealed class OrderPriceRestriction : IPriceRestriction
    {
        private readonly int _bandTicks;
        private long? _referencePriceTicks;

        internal OrderPriceRestriction(int bandTicks)
        {
            _bandTicks = bandTicks;
        }

        public RestrictionScope Scope => RestrictionScope.OrderEntry;
        public RestrictionBreachAction OnBreach => RestrictionBreachAction.Reject;

        // A rejection interrupts nothing, so there is nothing to resume from.
        public TimeSpan? ResumeAfter => null;

        // Inactive until it has a reference to measure from. A band with no width is not modelled
        // here at all - a security that wants none leaves the restriction out.
        public bool Allows(long priceTicks, DateTime time) =>
            !_referencePriceTicks.HasValue ||
            Math.Abs(priceTicks - _referencePriceTicks.Value) <= _bandTicks;

        public void OnTrade(long priceTicks, DateTime time) => _referencePriceTicks = priceTicks;

        public void OnSessionChange(long? referencePriceTicks)
        {
            if (referencePriceTicks.HasValue)
                _referencePriceTicks = referencePriceTicks;
        }
    }
}
