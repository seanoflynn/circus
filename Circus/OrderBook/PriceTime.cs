using System;
using System.Collections.Generic;

namespace Circus.OrderBook
{
    // Continuous trading under price-time priority. The aggressor trades against the FIFO-earliest
    // order at the best crossing level - taking the head, and only moving on once it is consumed,
    // is the time-priority rule - at that order's own limit price, so an aggressor whose limit was
    // better than the touch gets the improvement.
    //
    // Sized off both sides' displayed quantity: an iceberg shows the book one peak's worth at a
    // time and replenishes (losing queue priority) once that peak is exhausted.
    internal sealed class PriceTime : IMatchingAlgorithm
    {
        // Nothing to derive up front - each trade is priced and sized from the two orders at the
        // touch - and whether anything crosses is the matching loop's own question to answer.
        public bool TryBegin(IReadOnlyDictionary<Side, PriceLadder> working) => true;

        public Allocation? SelectNext(InternalOrder restingHead, InternalOrder aggressor) =>
            new Allocation(restingHead,
                Math.Min(restingHead.DisplayedQuantity, aggressor.DisplayedQuantity),
                restingHead.Price ?? throw new InvalidOperationException("limit order requires price"));

        public bool UsesFullRemainingQuantity => false;

        public bool ChecksTradeRestrictions => true;
    }
}
