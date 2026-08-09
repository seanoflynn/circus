using Circus.Events;
using Circus.Matching;

namespace Circus;

// What one action did to the displayed book, as the venue reports it: which price levels moved,
// which orders moved, and what printed.
//
// These three are the book's own view of itself rather than anything a participant was told, and
// they are derived rather than emitted as they happen - the level diff from the ladders either
// side of the action, the order changes and the prints by reading back the confirmations the
// action just produced. Doing it in one place at the end costs a walk of a list that already
// exists and saves a hook at every mutation site, which is why the book builds its events first
// and reports on them second.
//
// It lives outside OrderBook because none of it is about matching. What it needs is two windows,
// an event list and an instant; what it produces is three kinds of event. The book supplies the
// windows because only the book has ladders, and that is the whole of the coupling - this holds
// no reference to a book, a matcher or a ladder between calls.
internal sealed class DisplayedBookReport
{
    private readonly string _symbol;
    private readonly decimal _tickSize;

    // The published window as it stood before the action and as it stands after, diffed to produce
    // LevelsChanged. Held rather than allocated per call for the reason the book holds its own
    // buffers: one action at a time, so four lists outlive every call.
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

    // The window as it stands now, to be diffed against the one Append captures. When to call it
    // is the caller's business - see OrderBook.Process, which does so before anything the action
    // can reach, a resumption included.
    public void CaptureBefore(IReadOnlyPriceLadder bids, IReadOnlyPriceLadder offers)
    {
        bids.CopyLevelsFromBest(OrderBook.PublishedDepth, _bidsBefore);
        offers.CopyLevelsFromBest(OrderBook.PublishedDepth, _offersBefore);
    }

    // Appends every report the action earned, in the order a consumer wants them: what the levels
    // did, what the orders did, then what printed.
    //
    // Reports the net effect of the whole action rather than each state it passed through - an
    // aggressor sweeping three levels leaves one report per level touched, not one per fill along
    // the way. That is the shape of a real incremental refresh, which carries every level a single
    // matching-engine event moved.
    public void Append(List<OrderBookEvent> events, DateTime time,
        IReadOnlyPriceLadder bids, IReadOnlyPriceLadder offers)
    {
        AppendLevelChanges(events, time, bids, offers);
        AppendOrderChanges(events, time);
        AppendTradePrints(events, time);
    }

    // One event carrying every level the action moved within the published window, and none at all
    // for an action that moved nothing in it - a status change or a rejected order says nothing
    // here.
    //
    // One window, always OrderBook.PublishedDepth deep. A venue wanting to show a subscriber fewer
    // levels than that filters what it holds; it cannot be given a shallower delta stream, because
    // a delta does not truncate - see LevelsChanged for the case that makes that wrong, and
    // LevelWindowDiffTests for it asserted.
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

    // Diffed by price rather than by position - see LevelChange for why - which also means a level
    // that only moved rank contributes nothing, since its price, size and count are all unchanged.
    // Ten a side at most in the usual case, so the nested scans below are bounded at a hundred
    // comparisons and need no index to beat that.
    //
    // depth is where this window ends. The lists are the deepest window, so a shallower report
    // reads a prefix of them at both ends - a level below the cut is outside the window and is
    // neither reported nor available to match against, which is exactly what makes a level pushed
    // past the cut a departure rather than nothing at all.
    //
    // Static, and internal rather than private, because this is the part worth testing on its own:
    // two windows and a depth in, a set of changes out, with no book to drive and no order flow to
    // arrange in order to reach it.
    internal static void CollectLevelChanges(ref List<LevelChange>? changes, Side side,
        List<(long Tick, int Quantity, int Count)> before, List<(long Tick, int Quantity, int Count)> after,
        int depth, decimal tickSize)
    {
        var beforeCount = Math.Min(before.Count, depth);
        var afterCount = Math.Min(after.Count, depth);

        // Arrivals and changes first, best price outward, so a consumer applying them in order
        // builds the near side of the book before the far side.
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

        // Then departures, carrying the rank each last held and nothing left at it.
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

    // The displayed book's own view of what the confirmations did, read back off them.
    //
    // Derived rather than hooked at each mutation site because the confirmations already carry
    // exactly what it takes - PreviousExchangeOrderId to tell a requeue from an in-place modify,
    // PreviousPrice to tell an arrival from a move, PreviousQuantity for what left a level. Those
    // fields exist for this, and reading them once at the end costs a walk of a list the action
    // just built rather than a hook in six places.
    //
    // Only the count of events read is scanned, so an action that touched no order says nothing.
    private void AppendOrderChanges(List<OrderBookEvent> events, DateTime time)
    {
        List<OrderChange>? changes = null;

        // Snapshot the count first: this appends to the same list it is reading.
        var count = events.Count;
        for (var i = 0; i < count; i++)
        {
            switch (events[i])
            {
                // One per side of a trade, each its own change, paired by the id they share.
                case FillOrderConfirmed fill:
                    Add(ref changes, new OrderChange(fill.Order.Side, fill.Order.ExchangeOrderId,
                        fill.Price, fill.Quantity, OrderChangeAction.Filled, fill.TradeId));
                    break;

                // A move between levels. Losing time priority mints a fresh ExchangeOrderId, and a
                // consumer rebuilding the queue needs to see the old id leave and a new one arrive
                // at the back rather than a price change against an id that kept its place.
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

                // A stop still hidden is not on the displayed book, so it says nothing here.
                case CreateOrderConfirmed {Order.Status: not OrderStatus.Hidden} create:
                    Add(ref changes, new OrderChange(create.Order.Side, create.Order.ExchangeOrderId,
                        create.Order.Price!.Value, create.Order.DisplayedQuantity, OrderChangeAction.Added));
                    break;

                // No previous price means it was not on the displayed book before - a stop
                // triggering into it, which is an arrival rather than a move. Still hidden, and it
                // has not arrived yet.
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

    // One print per trade, from the pair of fills that share its id. The fills arrive adjacent, so
    // remembering the last id seen is enough to take the first of each pair and skip the second.
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
