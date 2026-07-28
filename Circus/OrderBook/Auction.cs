using System;

namespace Circus.OrderBook
{
    // A call-auction print. Every trade clears at one price - computed beforehand by
    // Matcher.TryComputeAuctionPrice as the price maximizing executable volume across the resting
    // book - and is sized off each side's full remaining quantity rather than its displayed peak:
    // the print is a single atomic allocation, not a sequence of continuous touches an iceberg
    // would need to ration its displayed size across.
    //
    // Constructed per print, since the clearing price belongs to the particular book state being
    // uncrossed.
    internal sealed class Auction : IMatchingAlgorithm
    {
        private readonly long _clearingPriceTicks;

        public Auction(long clearingPriceTicks)
        {
            _clearingPriceTicks = clearingPriceTicks;
        }

        public long PriceTicks(InternalOrder resting) => _clearingPriceTicks;

        public int Quantity(InternalOrder resting, InternalOrder aggressor) =>
            Math.Min(resting.RemainingQuantity, aggressor.RemainingQuantity);

        public bool UsesFullRemainingQuantity => true;

        // The print is itself the resolution mechanism for a crossed book - not something a
        // volatility pause should interrupt partway through.
        public bool ChecksTradeRestrictions => false;
    }
}
