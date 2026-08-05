using Circus.Events;

namespace Circus.MarketData;

// Everything a venue publishes about one instrument, assembled in one place. A book's events go
// in, the messages a subscriber would receive come out.
//
// One bundle per instrument. Only the status producer still accumulates anything - the rest are
// pure functions of the events they are handed, since the book now reports which price levels
// moved rather than leaving a producer to work it out by shadowing the book.
//
// Which products it carries is configured, because a venue is not one shape. CME channels carry
// by-price and by-order together with trades and status; Eurex splits by-order onto EOBI and
// by-price onto EMDI, publishing state on both; an ITCH-shaped venue carries by-order alone. All
// of them are this class with different flags, which is the point of the flags existing.
//
// Everything by default: a caller who has not thought about channels sees the whole venue, which
// is more than any real depth feed carries and the useful answer for a simulator.
//
// How deep the by-price products run is not here. That is the book's, since the book is what
// reports the levels, and a feed publishes as much of what it is given as it wants.
public sealed class InstrumentFeed
{
    private readonly MarketByPriceIncrementalProducer _levels = new();
    private readonly MarketByOrderIncrementalProducer _orderByOrder = new();
    private readonly TradeDataProducer _trades = new();
    private readonly IndicativePriceDataProducer _indicative = new();
    private readonly InstrumentStatusDataProducer _status = new();

    // The other half of what a venue publishes, on its own stream: where the book is, rather than
    // what changed about it. Each republishes the same message type its incremental counterpart
    // does, so a subscriber applies a snapshot the same way it applies an update.
    private readonly MarketByPriceSnapshotProducer _levelsSnapshot = new();
    private readonly InstrumentStatusSnapshotProducer _statusSnapshot = new();
    private readonly IndicativePriceSnapshotProducer _indicativeSnapshot = new();
    private readonly MarketByOrderSnapshotProducer _orderByOrderSnapshot = new();

    private readonly FeedProducts _products;

    public InstrumentFeed(string symbol, FeedProducts products = FeedProducts.All)
    {
        if (products == FeedProducts.None)
            throw new ArgumentException(
                "a feed carrying no products publishes nothing, which is a channel that should " +
                "not have been created rather than one that is quiet", nameof(products));

        Symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
        _products = products;
    }

    public string Symbol { get; }

    // What this feed carries. A channel is a subset of the venue in two directions - which
    // instruments, and which products about them - and this is the second.
    public FeedProducts Products => _products;

    private bool Carries(FeedProducts product) => (_products & product) != 0;

    // Ordering within one call is by producer, in the fixed order below, rather than interleaved
    // by time: every event in a single dispatch shares an instant, so there is no time order
    // among them to preserve. Across calls it is the order the venue dispatched them in, which is
    // the ordering that actually carries meaning.
    public IReadOnlyList<MarketDataEvent> Process(IReadOnlyList<OrderBookEvent> bookEvents)
    {
        var events = PublicHalf(bookEvents);
        if (events.Count == 0)
            return Array.Empty<MarketDataEvent>();

        List<MarketDataEvent>? output = null;

        if (Carries(FeedProducts.Status)) Collect(ref output, _status.Process(events));
        if (Carries(FeedProducts.Trades)) Collect(ref output, _trades.Process(events));
        if (Carries(FeedProducts.ByPrice)) Collect(ref output, _levels.Process(events));
        if (Carries(FeedProducts.ByOrder)) Collect(ref output, _orderByOrder.Process(events));
        if (Carries(FeedProducts.Indicative)) Collect(ref output, _indicative.Process(events));

        return output ?? (IReadOnlyList<MarketDataEvent>) Array.Empty<MarketDataEvent>();
    }

    // The snapshot half, kept a separate call rather than a second return value because the two
    // streams are numbered separately and published independently - a subscriber in sync reads
    // only the incremental one, and a channel is free to carry no snapshot feed at all.
    //
    // A dispatch that is not a snapshot tick produces nothing here, so the common path costs three
    // scans that find nothing rather than a branch the caller has to know to take.
    public IReadOnlyList<MarketDataEvent> Snapshot(IReadOnlyList<OrderBookEvent> bookEvents)
    {
        var events = PublicHalf(bookEvents);
        if (events.Count == 0)
            return Array.Empty<MarketDataEvent>();

        List<MarketDataEvent>? output = null;

        if (Carries(FeedProducts.Status)) Collect(ref output, _statusSnapshot.Process(events));
        if (Carries(FeedProducts.ByPrice)) Collect(ref output, _levelsSnapshot.Process(events));
        if (Carries(FeedProducts.ByOrder)) Collect(ref output, _orderByOrderSnapshot.Process(events));
        if (Carries(FeedProducts.Indicative)) Collect(ref output, _indicativeSnapshot.Process(events));

        return output ?? (IReadOnlyList<MarketDataEvent>) Array.Empty<MarketDataEvent>();
    }

    // The boundary where a book's output becomes a venue's. What a participant is told about its
    // own order stops here: producers are typed to MarketEvent, so this is the only place the two
    // halves are told apart, and it is a filter rather than a redaction because the public events
    // are their own events rather than copies with fields removed.
    private static IReadOnlyList<MarketEvent> PublicHalf(IReadOnlyList<OrderBookEvent> bookEvents)
    {
        List<MarketEvent>? publicEvents = null;

        for (var i = 0; i < bookEvents.Count; i++)
        {
            if (bookEvents[i] is MarketEvent marketEvent)
                (publicEvents ??= new List<MarketEvent>(bookEvents.Count)).Add(marketEvent);
        }

        return publicEvents ?? (IReadOnlyList<MarketEvent>) Array.Empty<MarketEvent>();
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