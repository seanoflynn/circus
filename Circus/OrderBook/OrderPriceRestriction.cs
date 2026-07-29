using System;

namespace Circus.OrderBook
{
    // The hard band checked at order entry. Sees only client-supplied resting limit prices - trigger
    // and Market/MarketLimit prices are governed elsewhere in InMemoryOrderBook.
    //
    // The reference it measures from follows CME's banding reference price, which moves with the
    // market state: the settlement price pre-open, the indicative price once an auction is quoting
    // one, and the last trade during continuous trading. Expressed as a precedence between three
    // anchors rather than as a rule about phases, which comes out the same without this needing to
    // know what phase the book is in - continuous trading publishes no indicative price, so the top
    // rung is simply empty there.
    internal sealed class OrderPriceRestriction : IPriceRestriction
    {
        private readonly int _bandTicks;

        private long? _indicativePriceTicks;
        private long? _lastTradePriceTicks;
        private long? _sessionPriceTicks;

        internal OrderPriceRestriction(int bandTicks)
        {
            _bandTicks = bandTicks;
        }

        public RestrictionScope Scope => RestrictionScope.OrderEntry;
        public RestrictionBreachAction OnBreach => RestrictionBreachAction.Reject;

        // A rejection interrupts nothing, so there is nothing to resume from.
        public TimeSpan? ResumeAfter => null;

        private long? ReferencePriceTicks =>
            _indicativePriceTicks ?? _lastTradePriceTicks ?? _sessionPriceTicks;

        // Inactive until some anchor exists. A band with no width is not modelled here at all - a
        // security that wants none leaves the restriction out.
        public bool Allows(long priceTicks, DateTime time)
        {
            var reference = ReferencePriceTicks;
            return !reference.HasValue || Math.Abs(priceTicks - reference.Value) <= _bandTicks;
        }

        // CME governs the gap between a stop's trigger and limit prices with the same band width
        // that governs how far an order may be priced from the reference.
        public bool AllowsStopSpread(long spreadTicks) => spreadTicks <= _bandTicks;

        public void OnTrade(long priceTicks, DateTime time) => _lastTradePriceTicks = priceTicks;

        // Null withdraws the quote, dropping the reference back to the last trade.
        public void OnIndicativePrice(long? priceTicks) => _indicativePriceTicks = priceTicks;

        // An explicit reference means "start from here", so it clears what it supersedes rather than
        // sitting underneath it: a new session's settlement price has to beat the previous session's
        // last trade, and it could never do that from the bottom of the precedence.
        public void OnSessionChange(long? referencePriceTicks)
        {
            if (!referencePriceTicks.HasValue)
                return;

            _sessionPriceTicks = referencePriceTicks;
            _lastTradePriceTicks = null;
            _indicativePriceTicks = null;
        }
    }
}
