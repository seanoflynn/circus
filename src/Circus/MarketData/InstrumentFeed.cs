using Circus.Events;

namespace Circus.MarketData;

public sealed class InstrumentFeed
{
    private readonly MarketByPriceIncrementalProducer _levels;
    private readonly MarketByOrderIncrementalProducer _orderByOrder = new();
    private readonly TradeDataProducer _trades = new();
    private readonly IndicativePriceDataProducer _indicative = new();

    private readonly MarketByPriceSnapshotProducer _levelsSnapshot;
    private readonly InstrumentStatusSnapshotProducer _statusSnapshot = new();
    private readonly IndicativePriceSnapshotProducer _indicativeSnapshot = new();
    private readonly MarketByOrderSnapshotProducer _orderByOrderSnapshot = new();

    private readonly FeedProducts _products;
    private readonly int _depth;
    private readonly int _snapshotEvery;

    private long _snapshotTicks;

    public InstrumentFeed(string symbol, FeedProducts products = FeedProducts.All,
        int depth = OrderBook.DefaultPublishedDepth, int snapshotEvery = 1)
    {
        if (products == FeedProducts.None)
            throw new ArgumentException(
                "a feed carrying no products publishes nothing, which is a channel that should " +
                "not have been created rather than one that is quiet", nameof(products));

        if (depth <= 0)
            throw new ArgumentOutOfRangeException(nameof(depth), depth,
                "a feed carrying no levels is not a by-price feed");

        if (snapshotEvery <= 0)
            throw new ArgumentOutOfRangeException(nameof(snapshotEvery), snapshotEvery,
                "a feed that skips every tick has no snapshot stream - leave the group's " +
                "snapshot interval unset instead, which says so");

        Symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
        _products = products;
        _depth = depth;
        _snapshotEvery = snapshotEvery;

        _levels = new MarketByPriceIncrementalProducer(depth);
        _levelsSnapshot = new MarketByPriceSnapshotProducer(depth);
    }

    public string Symbol { get; }

    public FeedProducts Products => _products;

    public int Depth => _depth;

    public int SnapshotEvery => _snapshotEvery;

    private bool Carries(FeedProducts product) => (_products & product) != 0;

    public IReadOnlyList<MarketDataEvent> Process(IReadOnlyList<OrderBookEvent> bookEvents)
    {
        var events = PublicHalf(bookEvents);
        if (events.Count == 0)
            return Array.Empty<MarketDataEvent>();

        List<MarketDataEvent>? output = null;

        if (Carries(FeedProducts.Status)) Collect(ref output, StatusOf(events));
        if (Carries(FeedProducts.Trades)) Collect(ref output, _trades.Process(events));
        if (Carries(FeedProducts.ByPrice)) Collect(ref output, _levels.Process(events));
        if (Carries(FeedProducts.ByOrder)) Collect(ref output, _orderByOrder.Process(events));
        if (Carries(FeedProducts.Indicative)) Collect(ref output, _indicative.Process(events));

        return output ?? (IReadOnlyList<MarketDataEvent>) Array.Empty<MarketDataEvent>();
    }

    public IReadOnlyList<MarketDataEvent> Snapshot(IReadOnlyList<OrderBookEvent> bookEvents)
    {
        var events = PublicHalf(bookEvents);
        if (events.Count == 0)
            return Array.Empty<MarketDataEvent>();

        if (!IsSnapshotTick(events))
            return Array.Empty<MarketDataEvent>();

        if (++_snapshotTicks % _snapshotEvery != 0)
            return Array.Empty<MarketDataEvent>();

        List<MarketDataEvent>? output = null;

        if (Carries(FeedProducts.Status)) Collect(ref output, _statusSnapshot.Process(events));
        if (Carries(FeedProducts.ByPrice)) Collect(ref output, _levelsSnapshot.Process(events));
        if (Carries(FeedProducts.ByOrder)) Collect(ref output, _orderByOrderSnapshot.Process(events));
        if (Carries(FeedProducts.Indicative)) Collect(ref output, _indicativeSnapshot.Process(events));

        return output ?? (IReadOnlyList<MarketDataEvent>) Array.Empty<MarketDataEvent>();
    }

    private static IList<InstrumentStatusDataEvent> StatusOf(IReadOnlyList<MarketEvent> events)
    {
        List<InstrumentStatusDataEvent>? output = null;

        foreach (var ev in events)
        {
            InstrumentStatusDataEvent? data = ev switch
            {
                StatusChanged status => new InstrumentStatusDataEvent(status.Symbol, status.Time,
                    status.Status, status.Reason, status.ResumesAt, status.LimitState),

                LimitStateChanged limit => new InstrumentStatusDataEvent(limit.Symbol, limit.Time,
                    limit.Status, limit.Reason, limit.ResumesAt, limit.Side),

                _ => null
            };

            if (data != null)
                (output ??= new List<InstrumentStatusDataEvent>()).Add(data);
        }

        return output ?? (IList<InstrumentStatusDataEvent>) Array.Empty<InstrumentStatusDataEvent>();
    }

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

    private static bool IsSnapshotTick(IReadOnlyList<MarketEvent> events)
    {
        for (var i = 0; i < events.Count; i++)
        {
            if (events[i] is BookSnapshot)
                return true;
        }

        return false;
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