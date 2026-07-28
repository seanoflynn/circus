using System;

namespace Circus.OrderBook
{
    // Selected by InMemoryOrderBook per phase (continuous vs. an auction print) and consulted by
    // Matcher.Run for how to price and size the next trade. The loop itself - self-match
    // detection, the crossing condition, stop-triggering - is identical either way and stays owned
    // by Matcher; only these decisions vary between algorithms.
    internal interface IMatchingAlgorithm
    {
        // The price a trade between these two orders should print at.
        long PriceTicks(InternalOrder resting);

        // How much of that trade should execute.
        int Quantity(InternalOrder resting, InternalOrder aggressor);

        // Whether a fill under this algorithm allocates against an order's full remaining quantity
        // (one atomic auction allocation) rather than its displayed peak (a continuous touch, which
        // an iceberg needs to ration across several) - tells Apply which InternalOrder fill method
        // to use.
        bool UsesFullRemainingQuantity { get; }

        // Whether a fill under this algorithm is checked against Trade-scoped price restrictions -
        // true for continuous matching; false for an auction print, which is already the
        // resolution mechanism, not something to interrupt.
        bool ChecksTradeRestrictions { get; }
    }

    // Price-time priority against each order's own resting limit price, sized off its displayed
    // peak - an iceberg only ever shows one peak's worth to the rest of the book at a time.
    internal sealed class ContinuousMatch : IMatchingAlgorithm
    {
        // Stateless - safe to share, including as the algorithm Run switches back to mid-sweep
        // once a stop triggers during an otherwise-Uncross print.
        public static readonly ContinuousMatch Instance = new();

        public long PriceTicks(InternalOrder resting) =>
            resting.Price ?? throw new InvalidOperationException("limit order requires price");

        public int Quantity(InternalOrder resting, InternalOrder aggressor) =>
            Math.Min(resting.DisplayedQuantity, aggressor.DisplayedQuantity);

        public bool UsesFullRemainingQuantity => false;
        public bool ChecksTradeRestrictions => true;
    }

    // The call-auction uncrossing pass: every trade prints at the single pre-computed clearing
    // price, sized off each side's full remaining quantity - the print is one atomic allocation,
    // not a sequence of continuous touches an iceberg would otherwise need to ration its displayed
    // size across.
    internal sealed class Uncross : IMatchingAlgorithm
    {
        private readonly long _priceTicks;

        public Uncross(long priceTicks)
        {
            _priceTicks = priceTicks;
        }

        public long PriceTicks(InternalOrder resting) => _priceTicks;

        public int Quantity(InternalOrder resting, InternalOrder aggressor) =>
            Math.Min(resting.RemainingQuantity, aggressor.RemainingQuantity);

        public bool UsesFullRemainingQuantity => true;
        public bool ChecksTradeRestrictions => false;
    }
}
