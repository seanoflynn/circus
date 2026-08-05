using Circus.Events;

namespace Circus.MarketData;

// The snapshot feed, as a translation of the BookSnapshot the book answers a snapshot tick with.
//
// Each of these republishes exactly the message type its incremental counterpart publishes, so a
// subscriber applies a snapshot the same way it applies an update and needs no second code path -
// the difference is which stream it arrived on and the sequence it declares itself consistent as
// of, both of which the channel carries. CME draws it the same way: the snapshot is a different
// message on a different feed, carrying the same fields.
//
// One class per product rather than one producing all three, because a channel carrying only
// depth should be able to leave the rest out - the same reason InstrumentFeed is a bundle of
// producers rather than one that does everything.
//
// The one place a feed shallower than its book truncates rather than subscribing. An image says
// where the book is, so the first five entries of a ten-deep image are the five-deep image and
// nothing is lost by cutting. A delta is the opposite - see LevelsChanged - which is why the
// incremental half takes reports made at its depth instead.
public class MarketByPriceSnapshotProducer : IIncrementalProducer<LevelsDataEvent>
{
    private readonly int _depth;

    public MarketByPriceSnapshotProducer(int depth = OrderBook.DefaultPublishedDepth)
    {
        if (depth <= 0)
            throw new ArgumentOutOfRangeException(nameof(depth), depth,
                "a feed carrying no levels is not a by-price feed");

        _depth = depth;
    }

    public int Depth => _depth;

    public IList<LevelsDataEvent> Process(IReadOnlyList<MarketEvent> events)
    {
        List<LevelsDataEvent>? output = null;

        foreach (var ev in events)
        {
            if (ev is not BookSnapshot snapshot)
                continue;

            output ??= new List<LevelsDataEvent>();
            output.Add(new LevelsDataEvent(snapshot.Symbol, snapshot.Time, _depth,
                Truncate(snapshot.Bids), Truncate(snapshot.Offers)));
        }

        return output ?? (IList<LevelsDataEvent>) Array.Empty<LevelsDataEvent>();
    }

    // The book's list unchanged when it is already within the window, so the common case - a feed
    // as deep as the book that feeds it - copies nothing.
    private IReadOnlyList<Level> Truncate(IReadOnlyList<Level> levels)
    {
        if (levels.Count <= _depth)
            return levels;

        var window = new List<Level>(_depth);
        for (var i = 0; i < _depth; i++)
            window.Add(levels[i]);

        return window;
    }
}

// The composite a joiner cannot rebuild: a status change and a limit lock arrive as separate
// events at separate times, so a subscriber that heard neither has no way to assemble them. This
// is what makes InstrumentStatusDataProducer's accumulator recoverable rather than merely stateful.
public class InstrumentStatusSnapshotProducer : IIncrementalProducer<InstrumentStatusDataEvent>
{
    public IList<InstrumentStatusDataEvent> Process(IReadOnlyList<MarketEvent> events)
    {
        List<InstrumentStatusDataEvent>? output = null;

        foreach (var ev in events)
        {
            if (ev is not BookSnapshot snapshot)
                continue;

            output ??= new List<InstrumentStatusDataEvent>();
            output.Add(new InstrumentStatusDataEvent(snapshot.Symbol, snapshot.Time, snapshot.Status,
                snapshot.StatusReason, snapshot.ResumesAt, snapshot.LimitState));
        }

        return output ?? (IList<InstrumentStatusDataEvent>) Array.Empty<InstrumentStatusDataEvent>();
    }
}

// Published on the cycle even when there is no quote, so a null price restates that the book is
// not crossing rather than leaving a joiner to assume it. The incremental feed says the same thing
// by emitting a withdrawal, which is a message a late subscriber never heard.
public class IndicativePriceSnapshotProducer : IIncrementalProducer<IndicativePriceDataEvent>
{
    public IList<IndicativePriceDataEvent> Process(IReadOnlyList<MarketEvent> events)
    {
        List<IndicativePriceDataEvent>? output = null;

        foreach (var ev in events)
        {
            if (ev is not BookSnapshot snapshot)
                continue;

            output ??= new List<IndicativePriceDataEvent>();
            output.Add(new IndicativePriceDataEvent(snapshot.Symbol, snapshot.Time,
                snapshot.IndicativePrice, snapshot.IndicativeQuantity));
        }

        return output ?? (IList<IndicativePriceDataEvent>) Array.Empty<IndicativePriceDataEvent>();
    }
}

// Every resting order, for a subscriber joining or recovering an order-by-order book. The
// heaviest message this venue publishes, and the reason a real snapshot feed cycles slowly.
public class MarketByOrderSnapshotProducer : IIncrementalProducer<OrdersDataEvent>
{
    public IList<OrdersDataEvent> Process(IReadOnlyList<MarketEvent> events)
    {
        List<OrdersDataEvent>? output = null;

        foreach (var ev in events)
        {
            if (ev is not BookSnapshot snapshot)
                continue;

            output ??= new List<OrdersDataEvent>();
            output.Add(new OrdersDataEvent(snapshot.Symbol, snapshot.Time, snapshot.Orders));
        }

        return output ?? (IList<OrdersDataEvent>) Array.Empty<OrdersDataEvent>();
    }
}
