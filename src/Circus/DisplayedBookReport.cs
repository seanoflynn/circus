using Circus.Events;
using Circus.Matching;

namespace Circus;

internal sealed class DisplayedBookReport
{
    private readonly string _symbol;
    private readonly decimal _tickSize;

    private readonly List<(long Tick, int Quantity, int Count)> _bidsBefore;
    private readonly List<(long Tick, int Quantity, int Count)> _offersBefore;
    private readonly List<(long Tick, int Quantity, int Count)> _bidsAfter;
    private readonly List<(long Tick, int Quantity, int Count)> _offersAfter;

    public DisplayedBookReport(string symbol, decimal tickSize)
    {
        _symbol = symbol;
        _tickSize = tickSize;

        _bidsBefore = new List<(long, int, int)>(OrderBook.PublishedDepth);
        _offersBefore = new List<(long, int, int)>(OrderBook.PublishedDepth);
        _bidsAfter = new List<(long, int, int)>(OrderBook.PublishedDepth);
        _offersAfter = new List<(long, int, int)>(OrderBook.PublishedDepth);
    }

    public void CaptureBefore(IReadOnlyPriceLadder bids, IReadOnlyPriceLadder offers)
    {
        bids.CopyLevelsFromBest(OrderBook.PublishedDepth, _bidsBefore);
        offers.CopyLevelsFromBest(OrderBook.PublishedDepth, _offersBefore);
    }

    public void Append(List<OrderBookEvent> events, DateTime time,
        IReadOnlyPriceLadder bids, IReadOnlyPriceLadder offers)
    {
        AppendLevelChanges(events, time, bids, offers);
        AppendOrderChanges(events, time);
        AppendTradePrints(events, time);
    }

    private void AppendLevelChanges(List<OrderBookEvent> events, DateTime time,
        IReadOnlyPriceLadder bids, IReadOnlyPriceLadder offers)
    {
        bids.CopyLevelsFromBest(OrderBook.PublishedDepth, _bidsAfter);
        offers.CopyLevelsFromBest(OrderBook.PublishedDepth, _offersAfter);

        List<LevelChange>? changes = null;
        CollectLevelChanges(ref changes, Side.Buy, _bidsBefore, _bidsAfter, OrderBook.PublishedDepth, _tickSize);
        CollectLevelChanges(ref changes, Side.Sell, _offersBefore, _offersAfter, OrderBook.PublishedDepth, _tickSize);

        if (changes != null)
            events.Add(new LevelsChanged(_symbol, time, OrderBook.PublishedDepth, changes));
    }

    internal static void CollectLevelChanges(ref List<LevelChange>? changes, Side side,
        List<(long Tick, int Quantity, int Count)> before, List<(long Tick, int Quantity, int Count)> after,
        int depth, decimal tickSize)
    {
        var beforeCount = Math.Min(before.Count, depth);
        var afterCount = Math.Min(after.Count, depth);

        for (var i = 0; i < afterCount; i++)
        {
            var (tick, quantity, count) = after[i];
            var previous = IndexOfTick(before, beforeCount, tick);

            if (previous < 0)
            {
                (changes ??= new List<LevelChange>()).Add(new LevelChange(side, i + 1, tick * tickSize,
                    quantity, count, LevelChangeAction.Added));
            }
            else if (before[previous].Quantity != quantity || before[previous].Count != count)
            {
                (changes ??= new List<LevelChange>()).Add(new LevelChange(side, i + 1, tick * tickSize,
                    quantity, count, LevelChangeAction.Modified));
            }
        }

        for (var i = 0; i < beforeCount; i++)
        {
            var tick = before[i].Tick;
            if (IndexOfTick(after, afterCount, tick) < 0)
                (changes ??= new List<LevelChange>()).Add(new LevelChange(side, i + 1, tick * tickSize,
                    0, 0, LevelChangeAction.Removed));
        }
    }

    private static int IndexOfTick(List<(long Tick, int Quantity, int Count)> levels, int count, long tick)
    {
        for (var i = 0; i < count; i++)
        {
            if (levels[i].Tick == tick)
                return i;
        }

        return -1;
    }

    private void AppendOrderChanges(List<OrderBookEvent> events, DateTime time)
    {
        List<OrderChange>? changes = null;

        // Snapshot the count first: this appends to the same list it is reading.
        var count = events.Count;
        for (var i = 0; i < count; i++)
        {
            switch (events[i])
            {
                case FillOrderConfirmed fill:
                    Add(ref changes, new OrderChange(fill.Order.Side, fill.Order.ExchangeOrderId,
                        fill.Price, fill.Quantity, OrderChangeAction.Filled, fill.TradeId));
                    break;

                case UpdateOrderConfirmed {PreviousPrice: { } movedFrom} moved:
                    if (moved.PreviousExchangeOrderId != moved.Order.ExchangeOrderId)
                    {
                        Add(ref changes, new OrderChange(moved.Order.Side, moved.PreviousExchangeOrderId,
                            movedFrom, moved.PreviousQuantity, OrderChangeAction.Removed));
                        Add(ref changes, new OrderChange(moved.Order.Side, moved.Order.ExchangeOrderId,
                            moved.Order.Price!.Value, moved.Order.DisplayedQuantity, OrderChangeAction.Added));
                    }
                    else
                    {
                        Add(ref changes, new OrderChange(moved.Order.Side, moved.Order.ExchangeOrderId,
                            moved.Order.Price!.Value, moved.Order.DisplayedQuantity, OrderChangeAction.Modified));
                    }

                    break;

                case CreateOrderConfirmed {Order.Status: not OrderStatus.Hidden} create:
                    Add(ref changes, new OrderChange(create.Order.Side, create.Order.ExchangeOrderId,
                        create.Order.Price!.Value, create.Order.DisplayedQuantity, OrderChangeAction.Added));
                    break;

                case UpdateOrderConfirmed {PreviousPrice: null, Order.Status: OrderStatus.Hidden}:
                    break;

                case UpdateOrderConfirmed {PreviousPrice: null} update:
                    Add(ref changes, new OrderChange(update.Order.Side, update.Order.ExchangeOrderId,
                        update.Order.Price!.Value, update.Order.DisplayedQuantity, OrderChangeAction.Added));
                    break;

                case CancelOrderConfirmed {PreviousPrice: { } cancelledAt} cancel:
                    Add(ref changes, new OrderChange(cancel.Order.Side, cancel.Order.ExchangeOrderId,
                        cancelledAt, cancel.PreviousQuantity, OrderChangeAction.Removed));
                    break;

                case ExpireOrderConfirmed {PreviousPrice: { } expiredAt} expire:
                    Add(ref changes, new OrderChange(expire.Order.Side, expire.Order.ExchangeOrderId,
                        expiredAt, expire.PreviousQuantity, OrderChangeAction.Removed));
                    break;
            }
        }

        if (changes != null)
            events.Add(new OrdersChanged(_symbol, time, changes));
    }

    private static void Add(ref List<OrderChange>? changes, OrderChange change) =>
        (changes ??= new List<OrderChange>()).Add(change);

    private void AppendTradePrints(List<OrderBookEvent> events, DateTime time)
    {
        List<OrderBookEvent>? prints = null;
        string? lastTradeId = null;

        var count = events.Count;
        for (var i = 0; i < count; i++)
        {
            if (events[i] is not FillOrderConfirmed fill || fill.TradeId == lastTradeId)
                continue;

            lastTradeId = fill.TradeId;
            (prints ??= new List<OrderBookEvent>()).Add(
                new TradePrinted(_symbol, time, fill.TradeId, fill.Price, fill.Quantity));
        }

        if (prints != null)
            events.AddRange(prints);
    }
}
