using Circus.Restrictions;

namespace Circus.Matching;

internal sealed class Matcher
{
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

    private readonly IReadOnlyDictionary<Side, IReadOnlyPriceLadder> _workingView;

    public Matcher()
    {
        _workingView = _working.ToDictionary(x => x.Key, x => (IReadOnlyPriceLadder) x.Value);
    }

    public IReadOnlyDictionary<Side, IReadOnlyPriceLadder> Working => _workingView;

    private static bool RestsInStops(InternalOrder order) =>
        order.Type is OrderType.StopLimit or OrderType.StopMarket;

    private PriceLadder LadderFor(InternalOrder order) =>
        RestsInStops(order) ? _stops[order.Side] : _working[order.Side];

    private static long RestingPriceOf(InternalOrder order) =>
        (RestsInStops(order) ? order.TriggerPrice : order.Price) ??
        throw new InvalidOperationException($"{order.Type} order missing the price it rests at");

    public void Rest(InternalOrder order) => LadderFor(order).Add(RestingPriceOf(order), order);

    public void Unrest(InternalOrder order) => LadderFor(order).Remove(order);

    // Must run before anything that unrests the order: Remove backs out whatever the order is
    // displaying at the time, so an uncorrected level would have the fill subtracted twice.
    public void SyncDisplayed(InternalOrder order, int previousDisplayedQuantity)
    {
        var delta = order.DisplayedQuantity - previousDisplayedQuantity;
        if (delta != 0)
            LadderFor(order).AdjustQuantity(order.RestingTick, delta);
    }

    public void Reprice(InternalOrder order, long newPriceTicks)
    {
        var ladder = LadderFor(order);
        ladder.Remove(order);
        ladder.Add(newPriceTicks, order);
    }

    private InternalOrder? BestOrder(Side side) =>
        _working[side].TryGetBest(out _, out var order) ? order : null;

    // The book is re-read every iteration, so the caller must apply each outcome - ladder mutation
    // included - before asking for the next. Buffering the sequence instead never terminates.
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
            var restingHead = buy.ModifiedTime < sell.ModifiedTime ? buy : sell;
            var aggressor = buy == restingHead ? sell : buy;

            var selection = algorithm.SelectNext(restingHead, aggressor);
            if (selection == null)
                yield break;

            var (resting, quantity, priceTicks) = selection.Value;

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

                algorithm = afterStopTrigger;
            }

            buy = BestOrder(Side.Buy);
            sell = BestOrder(Side.Sell);
        }
    }

    private List<InternalOrder>? GatherTriggeredStops(long priceTicks)
    {
        SortedDictionary<long, InternalOrder>? triggered = null;

        if (_stops[Side.Buy].TryGetBest(out var bestBuyStop, out _) && bestBuyStop <= priceTicks)
        {
            foreach (var (tick, first, _) in _stops[Side.Buy].EnumerateFromBest())
            {
                if (tick > priceTicks)
                    break;

                triggered ??= new SortedDictionary<long, InternalOrder>();
                for (var order = first; order != null; order = order.LevelNext)
                    triggered.Add(order.SequenceNumber, order);
            }
        }

        if (_stops[Side.Sell].TryGetBest(out var bestSellStop, out _) && bestSellStop >= priceTicks)
        {
            foreach (var (tick, first, _) in _stops[Side.Sell].EnumerateFromBest())
            {
                if (tick < priceTicks)
                    break;

                triggered ??= new SortedDictionary<long, InternalOrder>();
                for (var order = first; order != null; order = order.LevelNext)
                    triggered.Add(order.SequenceNumber, order);
            }
        }

        return triggered?.Values.ToList();
    }

    // Walks head-first, which SelectNext does not guarantee. Exact for any algorithm without
    // self-match interaction, and needs revisiting alongside one where visit order decides where
    // the self-match stop truncates the walk.
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
