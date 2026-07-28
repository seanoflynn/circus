using System;
using System.Collections.Generic;

namespace Circus.OrderBook
{
    // Continuous trading. Each trade prints at the resting order's own limit price - the
    // earlier-arrived side sets the price, so an aggressor whose limit was better than the touch
    // gets the improvement - and is sized off both sides' displayed quantity, since an iceberg
    // only ever shows one peak's worth to the book at a time and replenishes (losing queue
    // priority) once that peak is exhausted.
    internal sealed class PriceTime : IMatchingAlgorithm
    {
        // Nothing to derive up front - each trade is priced and sized from the two orders at the
        // touch - and whether anything crosses is the matching loop's own question to answer.
        public bool TryBegin(IReadOnlyDictionary<Side, PriceLadder> working) => true;

        public long PriceTicks(InternalOrder resting) =>
            resting.Price ?? throw new InvalidOperationException("limit order requires price");

        public int Quantity(InternalOrder resting, InternalOrder aggressor) =>
            Math.Min(resting.DisplayedQuantity, aggressor.DisplayedQuantity);

        public bool UsesFullRemainingQuantity => false;

        public bool ChecksTradeRestrictions => true;
    }
}
