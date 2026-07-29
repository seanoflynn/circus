using Circus.OrderBook.Restrictions;

namespace Circus.OrderBook.Matching;

// Owns the working and stop ladders outright: Rest, Unrest and Reprice are the only ways an
// order enters, leaves or moves within them, and the ladders never leave this class in a form
// anything else could write to.
//
// Run decides what should happen against that state but never mutates it or emits events;
// InMemoryOrderBook.Apply does both, between calls into Run's enumerator.
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

    // Projected once so what leaves this class can be read but not written. The stop ladders
    // are not projected at all - nothing outside needs to read them.
    private readonly IReadOnlyDictionary<Side, IReadOnlyPriceLadder> _workingView;

    public Matcher()
    {
        _workingView = _working.ToDictionary(x => x.Key, x => (IReadOnlyPriceLadder) x.Value);
    }

    public IReadOnlyDictionary<Side, IReadOnlyPriceLadder> Working => _workingView;

    // An untriggered stop rests in the stops ladder at its trigger price, everything else in
    // the working book at its limit price. Type rather than Status is the discriminator: these
    // run after Cancel/Expire/Fill has already overwritten Status, and ConvertToLimit retypes
    // an elected stop, so the two never disagree.
    private static bool RestsInStops(InternalOrder order) =>
        order.Type is OrderType.StopLimit or OrderType.StopMarket;

    private PriceLadder LadderFor(InternalOrder order) =>
        RestsInStops(order) ? _stops[order.Side] : _working[order.Side];

    private static long RestingPriceOf(InternalOrder order) =>
        (RestsInStops(order) ? order.TriggerPrice : order.Price) ??
        throw new InvalidOperationException($"{order.Type} order missing the price it rests at");

    public void Rest(InternalOrder order) => LadderFor(order).Add(RestingPriceOf(order), order);

    public void Unrest(InternalOrder order) => LadderFor(order).Remove(RestingPriceOf(order), order);

    // Lands at the back of the new level. Passing the price it already rests at requeues it in
    // place, which is what a quantity increase needs - losing time priority is the point.
    public void Reprice(InternalOrder order, long newPriceTicks)
    {
        var ladder = LadderFor(order);
        ladder.Remove(RestingPriceOf(order), order);
        ladder.Add(newPriceTicks, order);
    }

    private InternalOrder? BestOrder(Side side) =>
        _working[side].TryGetBest(out _, out var order) ? order : null;

    // One decision at a time: self-match cancellations, trades, restriction breaches and stop
    // triggers. The algorithm supplies counterparty and price; everything else here is the same
    // whichever one is active.
    //
    // The book is re-read every iteration, so the caller MUST apply each outcome - ladder
    // mutation included - before asking for the next. Buffering the sequence instead of
    // applying as you go never terminates. It is also what lets a converted stop landing in the
    // working book be picked up by this same loop rather than a recursive pass.
    //
    // afterStopTrigger takes over once a stop fires, and is assumed already prepared.
    // checkTradeRestrictionBreach reports the severest consequence among the Trade-scoped
    // restrictions disallowing a prospective price, and is consulted only when the algorithm
    // asks for it.
    public IEnumerable<MatchOutcome> Run(IMatchingAlgorithm algorithm, IMatchingAlgorithm afterStopTrigger,
        Func<long, RestrictionBreach?> checkTradeRestrictionBreach)
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
            // The older of the two orders at the touch is passive by definition, whatever the
            // algorithm then does with its level.
            var restingHead = buy.ModifiedTime < sell.ModifiedTime ? buy : sell;
            var aggressor = buy == restingHead ? sell : buy;

            var selection = algorithm.SelectNext(restingHead, aggressor);
            if (selection == null)
                yield break;

            var (resting, quantity, priceTicks) = selection.Value;

            // Against the order the algorithm picked, which need not be the head it was offered.
            if (IsSelfMatch(resting, aggressor, out var instruction))
            {
                yield return new SelfMatchDetected(resting, aggressor, instruction);
                buy = BestOrder(Side.Buy);
                sell = BestOrder(Side.Sell);
                continue;
            }

            if (algorithm.ChecksTradeRestrictions)
            {
                var breach = checkTradeRestrictionBreach(priceTicks);
                if (breach.HasValue)
                {
                    yield return new TradeRestrictionBreached(priceTicks, breach.Value);
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

    // Identifies only; Apply does the removing, converting and cancelling. Safe to call after
    // every trade, even repeatedly at one price, since an order an earlier StopsTriggered
    // already removed is not found again. Buy stops fire as price rises to their level and
    // sell stops as it falls, which is the direction each ladder is already ordered in.
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

    // Whether an incoming order would find at least `quantity` immediately fillable, for the
    // IOC minimum-quantity gate. The self-match arguments are the incoming order's own: a
    // resting match under CancelResting is skipped, but under CancelAggressor/CancelBoth the
    // incoming order would itself be cancelled there, so the walk must stop dead rather than
    // skip one order and keep summing.
    //
    // Walks head-first, which SelectNext no longer guarantees. A narrow assumption: total
    // reachable quantity is independent of visit order, and only the point at which the
    // self-match stop truncates the walk is not - so this stays exact for any algorithm
    // without self-match interaction, and needs revisiting alongside one where it differs.
    //
    // Not delegated to SelectNext: the gate runs before the incoming order is constructed, so
    // there is no aggressor to offer, and a stateful algorithm's per-level bookkeeping would
    // be polluted by a walk that never trades.
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
