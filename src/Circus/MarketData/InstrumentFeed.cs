using Circus.Events;

namespace Circus.MarketData;

// Everything a venue publishes about one instrument, assembled in one place. A book's events go
// in, the messages a subscriber would receive come out.
//
// One bundle per instrument. Only the status producer still accumulates anything - the rest are
// pure functions of the events they are handed, since the book now reports which price levels
// moved rather than leaving a producer to work it out by shadowing the book.
//
// No depth to configure. Every by-price product here is ten deep, fixed in the book that reports
// the levels, as CME's futures books are. Two depth streams merged into one channel would be
// indistinguishable anyway, so a venue publishing a five-deep and a ten-deep product does it as
// two feeds rather than one carrying both.
//
// A venue separating market-by-price from market-by-order would compose its own bundle rather
// than use this: both are here, which is the useful default for a simulator and more than a real
// depth feed carries.
public sealed class InstrumentFeed
{
    private readonly MarketByPriceIncrementalProducer _levels = new();
    private readonly FullBookDataProducer _orderByOrder = new();
    private readonly TradeDataProducer _trades = new();
    private readonly IndicativePriceDataProducer _indicative = new();
    private readonly InstrumentStatusDataProducer _status = new();

    // The other half of what a venue publishes, on its own stream: where the book is, rather than
    // what changed about it. Each republishes the same message type its incremental counterpart
    // does, so a subscriber applies a snapshot the same way it applies an update.
    private readonly MarketByPriceSnapshotProducer _levelsSnapshot = new();
    private readonly InstrumentStatusSnapshotProducer _statusSnapshot = new();
    private readonly IndicativePriceSnapshotProducer _indicativeSnapshot = new();

    public InstrumentFeed(string symbol)
    {
        Symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
    }

    public string Symbol { get; }

    // Ordering within one call is by producer, in the fixed order below, rather than interleaved
    // by time: every event in a single dispatch shares an instant, so there is no time order
    // among them to preserve. Across calls it is the order the venue dispatched them in, which is
    // the ordering that actually carries meaning.
    public IReadOnlyList<MarketDataEvent> Process(IReadOnlyList<OrderBookEvent> events)
    {
        if (events.Count == 0)
            return Array.Empty<MarketDataEvent>();

        List<MarketDataEvent>? output = null;

        Collect(ref output, _status.Process(events));
        Collect(ref output, _trades.Process(events));
        Collect(ref output, _levels.Process(events));
        Collect(ref output, _orderByOrder.Process(events));
        Collect(ref output, _indicative.Process(events));

        return output ?? (IReadOnlyList<MarketDataEvent>) Array.Empty<MarketDataEvent>();
    }

    // The snapshot half, kept a separate call rather than a second return value because the two
    // streams are numbered separately and published independently - a subscriber in sync reads
    // only the incremental one, and a channel is free to carry no snapshot feed at all.
    //
    // A dispatch that is not a snapshot tick produces nothing here, so the common path costs three
    // scans that find nothing rather than a branch the caller has to know to take.
    public IReadOnlyList<MarketDataEvent> Snapshot(IReadOnlyList<OrderBookEvent> events)
    {
        if (events.Count == 0)
            return Array.Empty<MarketDataEvent>();

        List<MarketDataEvent>? output = null;

        Collect(ref output, _statusSnapshot.Process(events));
        Collect(ref output, _levelsSnapshot.Process(events));
        Collect(ref output, _indicativeSnapshot.Process(events));

        return output ?? (IReadOnlyList<MarketDataEvent>) Array.Empty<MarketDataEvent>();
    }

    private static void Collect<T>(ref List<MarketDataEvent>? output, IList<T> produced)
        where T : MarketDataEvent
    {
        if (produced.Count == 0)
            return;

        output ??= new List<MarketDataEvent>();
        foreach (var data in produced)
            output.Add(data);
    }
}