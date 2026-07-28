using System;
using System.Collections.Generic;
using System.Linq;

namespace Circus.OrderBook
{
    // A call-auction print. Every trade clears at one price - the price maximizing executable
    // volume across the resting book, computed here - and is sized off each side's full remaining
    // quantity rather than its displayed peak: the print is a single atomic allocation, not a
    // sequence of continuous touches an iceberg would need to ration its displayed size across.
    internal sealed class Auction : IMatchingAlgorithm
    {
        // Reference anchor for the tie-break below, and nothing else: seeded from an explicit
        // reference price (mirroring CME's settlement price pre-open) before any trade, then
        // tracking the trade price. Owned here rather than by the book for the same reason each
        // IPriceRestriction owns its own anchor - no other concern reads it. Deliberately distinct
        // from the book's _lastTradedPrice, which being null specifically means "no trade yet" for
        // the stop-trigger checks.
        private long? _referencePriceTicks;

        // Fixed by TryBegin for the duration of one print, so the trades that print are allocated
        // against the book as it stood when the price was struck - not re-derived per trade from a
        // book those very trades are consuming.
        private long _clearingPriceTicks;

        // Lifecycle hooks mirroring IPriceRestriction.OnTrade / OnSessionChange, called by the
        // book to keep the anchor above current.
        public void OnTrade(long priceTicks) => _referencePriceTicks = priceTicks;

        public void OnSessionChange(long? referencePriceTicks) => _referencePriceTicks = referencePriceTicks;

        // False when the book isn't crossed, which is what makes an uncrossing pass over an
        // uncrossed book a no-op rather than something the caller has to check for first.
        public bool TryBegin(IReadOnlyDictionary<Side, IReadOnlyPriceLadder> working) =>
            TryComputeClearingPrice(working, out _clearingPriceTicks, out _);

        // Time priority at the clearing price: the FIFO-earliest order at the level fills first,
        // same order the continuous algorithm would take them in - what differs is the price they
        // all print at and the full-size allocation below.
        public Allocation? SelectNext(InternalOrder restingHead, InternalOrder aggressor) =>
            new Allocation(restingHead,
                Math.Min(restingHead.RemainingQuantity, aggressor.RemainingQuantity),
                _clearingPriceTicks);

        public bool UsesFullRemainingQuantity => true;

        // The print is itself the resolution mechanism for a crossed book - not something a
        // volatility pause should interrupt partway through.
        public bool ChecksTradeRestrictions => false;

        // The uncrossing price: the price that maximizes executable volume across the resting
        // book, i.e. min(cumulative bid quantity at/above p, cumulative ask quantity at/below p).
        // Ties break by (1) minimum surplus - CME's rule, (2) closest to _referencePriceTicks,
        // since neither venue's public docs cover a case this granular, (3) CME's final rule:
        // highest price if the surplus is on the buy side, lowest if on the sell side. Stops are
        // deliberately not folded into this search (unlike CME's iterative stop-election loop) -
        // they're picked up afterward by Matcher.Run's own stop-triggering check, same as any
        // other trade.
        //
        // Public and side-effect-free so the book can also publish it as a live indicative price
        // during pre-open, without committing to a print.
        public bool TryComputeClearingPrice(IReadOnlyDictionary<Side, IReadOnlyPriceLadder> working,
            out long priceTicks, out int quantity)
        {
            priceTicks = 0;
            quantity = 0;

            var buyLevels = working[Side.Buy].EnumerateFromBest()
                .Select(x => (Tick: x.Tick, Qty: SumRemaining(x.First))).ToList();
            var sellLevels = working[Side.Sell].EnumerateFromBest()
                .Select(x => (Tick: x.Tick, Qty: SumRemaining(x.First))).ToList();

            if (buyLevels.Count == 0 || sellLevels.Count == 0 || buyLevels[0].Tick < sellLevels[0].Tick)
                return false;

            var candidates = buyLevels.Select(l => l.Tick).Concat(sellLevels.Select(l => l.Tick)).Distinct();

            (long Price, int Executable, long Surplus)? best = null;
            foreach (var p in candidates)
            {
                var cumBid = buyLevels.Where(l => l.Tick >= p).Sum(l => l.Qty);
                var cumAsk = sellLevels.Where(l => l.Tick <= p).Sum(l => l.Qty);
                var executable = Math.Min(cumBid, cumAsk);
                var surplus = cumBid - cumAsk;

                if (best == null || executable > best.Value.Executable ||
                    (executable == best.Value.Executable &&
                     IsBetterTieBreak(p, surplus, best.Value.Price, best.Value.Surplus)))
                {
                    best = (p, executable, surplus);
                }
            }

            priceTicks = best!.Value.Price;
            quantity = best.Value.Executable;
            return quantity > 0;
        }

        private bool IsBetterTieBreak(long candidatePrice, long candidateSurplus, long currentPrice,
            long currentSurplus)
        {
            var candidateAbsSurplus = Math.Abs(candidateSurplus);
            var currentAbsSurplus = Math.Abs(currentSurplus);
            if (candidateAbsSurplus != currentAbsSurplus)
                return candidateAbsSurplus < currentAbsSurplus;

            if (_referencePriceTicks.HasValue)
            {
                var candidateDistance = Math.Abs(candidatePrice - _referencePriceTicks.Value);
                var currentDistance = Math.Abs(currentPrice - _referencePriceTicks.Value);
                if (candidateDistance != currentDistance)
                    return candidateDistance < currentDistance;
            }

            // surplus on the buy side (positive) -> prefer the higher price; sell side -> lower
            return candidateSurplus > 0 ? candidatePrice > currentPrice : candidatePrice < currentPrice;
        }

        // Counts an iceberg's hidden reserve in full - price discovery is on true size, unlike the
        // displayed-only aggregates the book publishes as market data.
        private static int SumRemaining(InternalOrder? first)
        {
            var total = 0;
            for (var order = first; order != null; order = order.LevelNext)
                total += order.RemainingQuantity;
            return total;
        }
    }
}
