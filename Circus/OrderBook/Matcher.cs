using System;
using System.Collections.Generic;
using System.Linq;

namespace Circus.OrderBook
{
    // Owns the resting-order state (working + stop ladders), decides what should happen against
    // that state via Run, and hosts the pure, state-reading decision helpers Run and
    // InMemoryOrderBook both need. Run only ever decides - it never mutates state or emits
    // events; InMemoryOrderBook.Apply does that, order by order, between calls into Run's
    // enumerator, so Run always resumes against state Apply has already caught up to.
    internal sealed class Matcher
    {
        // Array-backed, indexed by tick count (price / Security.TickSize) rather than decimal —
        // see InternalOrder and PriceLadder for why.
        private readonly Dictionary<Side, PriceLadder> _working = new()
        {
            {Side.Buy, new PriceLadder(descending: true)},
            {Side.Sell, new PriceLadder(descending: false)}
        };

        private readonly Dictionary<Side, PriceLadder> _stops = new()
        {
            {Side.Buy, new PriceLadder(descending: false)},
            {Side.Sell, new PriceLadder(descending: true)}
        };

        public IReadOnlyDictionary<Side, PriceLadder> Working => _working;
        public IReadOnlyDictionary<Side, PriceLadder> Stops => _stops;

        private InternalOrder? BestOrder(Side side) =>
            _working[side].TryGetBest(out _, out var order) ? order : null;

        // Decides, one step at a time, what Match should do next against the current book state -
        // self-match cancellations, trades, trade-restriction breaches, and stop triggers -
        // without mutating anything or emitting events. checkTradeRestrictionBreach is a pure
        // query (not consulted during an auction uncrossing pass, same as before) returning the
        // breach action of the first Trade-scoped restriction that disallows the prospective trade
        // price, if any. Re-reads the book fresh on every iteration, so the caller must fully
        // apply each yielded outcome - including any ladder mutation - before asking for the next
        // one; a converted stop landing in Working is exactly what lets this same loop pick it up
        // and keep matching, with no separate recursive pass needed.
        public IEnumerable<MatchOutcome> Run(long? auctionPriceTicks,
            Func<long, RestrictionBreachAction?> checkTradeRestrictionBreach)
        {
            // Once a stop fires mid-sweep, everything after it prices continuously - an auction
            // print is the resolution mechanism for the book as it stood at open, not for orders
            // that have just newly arrived because of it.
            var effectiveAuctionPriceTicks = auctionPriceTicks;

            var buy = BestOrder(Side.Buy);
            var sell = BestOrder(Side.Sell);

            if (buy != null && !buy.Price.HasValue)
                throw new InvalidOperationException("buy limit order requires price");
            if (sell != null && !sell.Price.HasValue)
                throw new InvalidOperationException("sell limit order requires price");

            while (buy != null && sell != null && buy.Price >= sell.Price)
            {
                var resting = buy.ModifiedTime < sell.ModifiedTime ? buy : sell;
                var aggressor = buy == resting ? sell : buy;

                if (IsSelfMatch(resting, aggressor, out var instruction))
                {
                    yield return new SelfMatchDetected(resting, aggressor, instruction);
                    buy = BestOrder(Side.Buy);
                    sell = BestOrder(Side.Sell);
                    continue;
                }

                var quantity = Math.Min(resting.DisplayedQuantity, aggressor.DisplayedQuantity);
                var priceTicks = effectiveAuctionPriceTicks ?? resting.Price ??
                    throw new InvalidOperationException("limit order requires price");

                // Checked against the prospective trade price, not either order's own submitted
                // limit price - a resting order sitting far from the market doesn't represent any
                // actual price movement, only an executed trade at an extreme price does. Doesn't
                // apply to an auction uncrossing pass itself (auctionPriceTicks set) - the auction
                // print is already the resolution mechanism, not something to interrupt.
                if (effectiveAuctionPriceTicks == null)
                {
                    var breachAction = checkTradeRestrictionBreach(priceTicks);
                    if (breachAction.HasValue)
                    {
                        yield return new TradeRestrictionBreached(priceTicks, breachAction.Value);
                        yield break;
                    }
                }

                yield return new TradeExecuted(resting, aggressor, priceTicks, quantity);

                var triggeredStops = GatherTriggeredStops(priceTicks);
                if (triggeredStops != null)
                {
                    yield return new StopsTriggered(triggeredStops);
                    effectiveAuctionPriceTicks = null;
                }

                buy = BestOrder(Side.Buy);
                sell = BestOrder(Side.Sell);
            }
        }

        // Non-mutating: only identifies which resting stop orders now qualify to trigger at
        // priceTicks - removing them from Stops, and converting or cancelling them, is Apply's
        // job. Safe to call after every trade, even repeatedly at the same price: an order already
        // removed from Stops by an earlier StopsTriggered within this same Run simply isn't found
        // again. Buy stops trigger as price rises to/through their level, sell stops as it falls
        // to/through theirs - same direction each ladder is already ordered in, so EnumerateFromBest
        // can stop at the first level that no longer qualifies.
        private List<InternalOrder>? GatherTriggeredStops(long priceTicks)
        {
            SortedDictionary<long, InternalOrder>? triggered = null;

            foreach (var (tick, first, _) in _stops[Side.Buy].EnumerateFromBest())
            {
                if (tick > priceTicks)
                    break;

                triggered ??= new SortedDictionary<long, InternalOrder>();
                for (var order = first; order != null; order = order.LevelNext)
                    triggered.Add(order.SequenceNumber, order);
            }

            foreach (var (tick, first, _) in _stops[Side.Sell].EnumerateFromBest())
            {
                if (tick < priceTicks)
                    break;

                triggered ??= new SortedDictionary<long, InternalOrder>();
                for (var order = first; order != null; order = order.LevelNext)
                    triggered.Add(order.SequenceNumber, order);
            }

            return triggered?.Values.ToList();
        }

        private static int SumRemaining(InternalOrder? first)
        {
            var total = 0;
            for (var order = first; order != null; order = order.LevelNext)
                total += order.RemainingQuantity;
            return total;
        }

        // The call-auction uncrossing price: the price that maximizes executable volume across
        // the resting book, i.e. min(cumulative bid quantity at/above p, cumulative ask quantity
        // at/below p). Ties break by (1) minimum surplus - CME's rule, (2) closest to
        // auctionReferencePriceTicks, since neither venue's public docs cover a case this granular,
        // (3) CME's final rule: highest price if the surplus is on the buy side, lowest if on the
        // sell side. Stops are deliberately not folded into this search (unlike CME's iterative
        // stop-election loop) - they're picked up afterward by Run's own stop-triggering check,
        // same as any other trade.
        public bool TryComputeAuctionPrice(long? auctionReferencePriceTicks, out long priceTicks, out int quantity)
        {
            priceTicks = 0;
            quantity = 0;

            var buyLevels = _working[Side.Buy].EnumerateFromBest()
                .Select(x => (Tick: x.Tick, Qty: SumRemaining(x.First))).ToList();
            var sellLevels = _working[Side.Sell].EnumerateFromBest()
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
                     IsBetterAuctionPriceTieBreak(p, surplus, best.Value.Price, best.Value.Surplus,
                         auctionReferencePriceTicks)))
                {
                    best = (p, executable, surplus);
                }
            }

            priceTicks = best!.Value.Price;
            quantity = best.Value.Executable;
            return quantity > 0;
        }

        private static bool IsBetterAuctionPriceTieBreak(long candidatePrice, long candidateSurplus,
            long currentPrice, long currentSurplus, long? auctionReferencePriceTicks)
        {
            var candidateAbsSurplus = Math.Abs(candidateSurplus);
            var currentAbsSurplus = Math.Abs(currentSurplus);
            if (candidateAbsSurplus != currentAbsSurplus)
                return candidateAbsSurplus < currentAbsSurplus;

            if (auctionReferencePriceTicks.HasValue)
            {
                var candidateDistance = Math.Abs(candidatePrice - auctionReferencePriceTicks.Value);
                var currentDistance = Math.Abs(currentPrice - auctionReferencePriceTicks.Value);
                if (candidateDistance != currentDistance)
                    return candidateDistance < currentDistance;
            }

            // surplus on the buy side (positive) -> prefer the higher price; sell side -> lower
            return candidateSurplus > 0 ? candidatePrice > currentPrice : candidatePrice < currentPrice;
        }

        // selfMatchPreventionId/selfMatchPreventionInstruction are the incoming order's own
        // fields. Walks resting orders in the same price/time priority order Match() would
        // actually consume them in: a self-matched order with CancelResting is simply skipped
        // (the incoming order keeps going, only the resting order would die), but with
        // CancelAggressor/CancelBoth the incoming order itself would be cancelled right there,
        // so nothing beyond that point can ever count - liquidity checking must stop dead,
        // not just exclude that one order's quantity and keep summing past it.
        public bool HasSufficientLiquidity(Side side, long priceTicks, int quantity, string? selfMatchPreventionId,
            SelfMatchPreventionInstruction? selfMatchPreventionInstruction)
        {
            var opposing = _working[side == Side.Buy ? Side.Sell : Side.Buy];
            var total = 0;
            foreach (var (tick, first, _) in opposing.EnumerateFromBest())
            {
                var crosses = side == Side.Buy ? tick <= priceTicks : tick >= priceTicks;
                if (!crosses)
                    break;

                for (var restingOrder = first; restingOrder != null; restingOrder = restingOrder.LevelNext)
                {
                    if (TryGetSelfMatchInstruction(restingOrder, selfMatchPreventionId,
                            selfMatchPreventionInstruction, out var instruction))
                    {
                        // total < quantity is guaranteed here - otherwise we'd have already
                        // returned true below before reaching this order
                        if (instruction != SelfMatchPreventionInstruction.CancelResting)
                            return false;

                        continue;
                    }

                    total += restingOrder.RemainingQuantity;
                    if (total >= quantity)
                        return true;
                }
            }

            return total >= quantity;
        }

        // Two orders are a prevented self-match only if both carry the same non-null
        // SelfMatchPreventionId - matches CME/Eurex, where this is a dedicated opt-in id
        // distinct from the firm/company identifier (so unrelated desks under one company
        // aren't blocked from trading each other).
        private static bool IsSelfMatch(InternalOrder resting, InternalOrder aggressor,
            out SelfMatchPreventionInstruction instruction) =>
            TryGetSelfMatchInstruction(resting, aggressor.SelfMatchPreventionId,
                aggressor.SelfMatchPreventionInstruction, out instruction);

        private static bool TryGetSelfMatchInstruction(InternalOrder resting, string? incomingSelfMatchPreventionId,
            SelfMatchPreventionInstruction? incomingInstruction, out SelfMatchPreventionInstruction instruction)
        {
            if (incomingSelfMatchPreventionId == null ||
                resting.SelfMatchPreventionId != incomingSelfMatchPreventionId)
            {
                instruction = default;
                return false;
            }

            instruction = incomingInstruction ?? resting.SelfMatchPreventionInstruction ??
                SelfMatchPreventionInstruction.CancelResting;
            return true;
        }
    }
}
