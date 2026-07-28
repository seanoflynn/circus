using System;

namespace Circus.OrderBook
{
    // Only client-supplied resting limit prices go through this - not trigger prices (already
    // governed by the TriggerPriceMustBe.../LastTradedPrice checks in InMemoryOrderBook) and not
    // the computed effective price for Market/MarketLimit orders (already governed by the
    // separate MarketOrderProtectionTicks mechanism). Inactive (always allows) until both a band
    // width is configured and a reference price has been established. Anchored on the last trade,
    // seeded from an explicit reference price pre-open.
    internal sealed class OrderPriceRestriction : IPriceRestriction
    {
        private readonly int? _bandTicks;
        private long? _referencePriceTicks;

        // Takes the band width rather than the Security it came from: nothing else about the
        // instrument is any of this restriction's business, and a test shouldn't have to invent
        // an instrument to configure one number.
        internal OrderPriceRestriction(int? bandTicks)
        {
            _bandTicks = bandTicks;
        }

        public RestrictionScope Scope => RestrictionScope.OrderEntry;
        public RestrictionBreachAction OnBreach => RestrictionBreachAction.Reject;

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
