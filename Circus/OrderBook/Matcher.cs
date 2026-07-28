using System;
using System.Collections.Generic;
using System.Linq;

namespace Circus.OrderBook
{
    // Owns the resting-order state - the working and stop ladders - outright: Rest, Unrest and
    // Reprice are the only ways an order enters, leaves or moves within them, and the ladders
    // themselves never leave this class in a form anything else could write to. Callers name the
    // order they want moved and this works out which ladder holds it and at what price, so the
    // rule that an untriggered stop rests at its trigger price lives in one place.
    //
    // Also decides what should happen against that state via Run, and hosts the pure,
    // state-reading decision helpers Run and InMemoryOrderBook both need. Run only ever decides -
    // it never mutates state or emits events; InMemoryOrderBook.Apply does that, order by order,
    // between calls into Run's enumerator, so Run always resumes against state Apply has already
    // caught up to.
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

        // Projected once, so what leaves this class can be read but not written. The stop ladders
        // are not projected at all - nothing outside has any business reading them, and everything
        // that used to reach in to move a stop now goes through Rest/Unrest below.
        private readonly IReadOnlyDictionary<Side, IReadOnlyPriceLadder> _workingView;

        public Matcher()
        {
            _workingView = _working.ToDictionary(x => x.Key, x => (IReadOnlyPriceLadder) x.Value);
        }

        public IReadOnlyDictionary<Side, IReadOnlyPriceLadder> Working => _workingView;

        // Which ladder an order rests in, and the price that ladder keys it by, both follow from
        // its type: an untriggered stop sits in the stops ladder at its trigger price, everything
        // else in the working book at its limit price. Type rather than Status is the discriminator
        // because these are routinely called after Cancel/Expire/Fill has already overwritten
        // Status - and ConvertToLimit moves an elected stop to Limit, so the two never disagree.
        private static bool RestsInStops(InternalOrder order) =>
            order.Type is OrderType.StopLimit or OrderType.StopMarket;

        private PriceLadder LadderFor(InternalOrder order) =>
            RestsInStops(order) ? _stops[order.Side] : _working[order.Side];

        private static long RestingPriceOf(InternalOrder order) =>
            (RestsInStops(order) ? order.TriggerPrice : order.Price) ??
            throw new InvalidOperationException($"{order.Type} order missing the price it rests at");

        public void Rest(InternalOrder order) => LadderFor(order).Add(RestingPriceOf(order), order);

        public void Unrest(InternalOrder order) => LadderFor(order).Remove(RestingPriceOf(order), order);

        // Moves an order to a new price within whichever ladder holds it, landing at the back of
        // the new level. Passing the price it already rests at requeues it in place, which is what
        // a quantity increase needs - losing time priority is the point, not a side effect.
        public void Reprice(InternalOrder order, long newPriceTicks)
        {
            var ladder = LadderFor(order);
            ladder.Remove(RestingPriceOf(order), order);
            ladder.Add(newPriceTicks, order);
        }

        private InternalOrder? BestOrder(Side side) =>
            _working[side].TryGetBest(out _, out var order) ? order : null;

        // Decides, one step at a time, what Match should do next against the current book state -
        // self-match cancellations, trades, trade-restriction breaches, and stop triggers -
        // without mutating anything or emitting events. algorithm picks the counterparty for each
        // trade and prices it (see IMatchingAlgorithm.cs); everything else here - the crossing
        // condition, which side is the aggressor, self-match detection, stop-triggering - is
        // identical regardless of which algorithm is active and stays here rather than being
        // reimplemented per algorithm.
        // afterStopTrigger is the algorithm the remainder of this run switches to once a
        // stop fires (see below) - the security's continuous algorithm, which for a run that was
        // already continuous is simply the same instance again; it is taken as already prepared,
        // never TryBegin'd mid-run.
        // checkTradeRestrictionBreach is a pure query (consulted only when
        // algorithm.ChecksTradeRestrictions) returning the breach action of the first Trade-scoped
        // restriction that disallows the prospective trade price, if any. Re-reads the book fresh
        // on every iteration, so the caller must fully apply each yielded outcome - including any
        // ladder mutation - before asking for the next one; a converted stop landing in Working is
        // exactly what lets this same loop pick it up and keep matching, with no separate
        // recursive pass needed.
        public IEnumerable<MatchOutcome> Run(IMatchingAlgorithm algorithm, IMatchingAlgorithm afterStopTrigger,
            Func<long, RestrictionBreachAction?> checkTradeRestrictionBreach)
        {
            if (!algorithm.TryBegin(_workingView))
                yield break;

            var buy = BestOrder(Side.Buy);
            var sell = BestOrder(Side.Sell);

            if (buy != null && !buy.Price.HasValue)
                throw new InvalidOperationException("buy limit order requires price");
            if (sell != null && !sell.Price.HasValue)
                throw new InvalidOperationException("sell limit order requires price");

            while (buy != null && sell != null && buy.Price >= sell.Price)
            {
                // Which side is passive is the loop's call, not the algorithm's - the older of the
                // two orders at the touch is resting by definition, whatever the algorithm then
                // does with its level.
                var restingHead = buy.ModifiedTime < sell.ModifiedTime ? buy : sell;
                var aggressor = buy == restingHead ? sell : buy;

                var selection = algorithm.SelectNext(restingHead, aggressor);
                if (selection == null)
                    yield break;

                var (resting, quantity, priceTicks) = selection.Value;

                // Checked against the order the algorithm actually picked, which for anything
                // other than price-time need not be the head it was offered.
                if (IsSelfMatch(resting, aggressor, out var instruction))
                {
                    yield return new SelfMatchDetected(resting, aggressor, instruction);
                    buy = BestOrder(Side.Buy);
                    sell = BestOrder(Side.Sell);
                    continue;
                }

                if (algorithm.ChecksTradeRestrictions)
                {
                    var breachAction = checkTradeRestrictionBreach(priceTicks);
                    if (breachAction.HasValue)
                    {
                        yield return new TradeRestrictionBreached(priceTicks, breachAction.Value);
                        yield break;
                    }
                }

                yield return new TradeExecuted(resting, aggressor, priceTicks, quantity,
                    algorithm.UsesFullRemainingQuantity);

                var triggeredStops = GatherTriggeredStops(priceTicks);
                if (triggeredStops != null)
                {
                    yield return new StopsTriggered(triggeredStops);

                    // Once a stop fires mid-sweep, everything after it prices continuously - an
                    // auction print is the resolution mechanism for the book as it stood at open,
                    // not for orders that have just newly arrived because of it.
                    algorithm = afterStopTrigger;
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
