using Circus.Events;

namespace Circus.MarketData;

public sealed class InstrumentFeed
{
    private readonly (FeedProducts Product, Func<MarketEvent, MarketDataEvent?> Project)[] _incremental;

    private readonly (FeedProducts Product, Func<BookSnapshot, MarketDataEvent> Project)[] _snapshot;

    private readonly FeedProducts _products;
    private readonly int _snapshotEvery;

    private long _snapshotTicks;

    public InstrumentFeed(string symbol, FeedProducts products = FeedProducts.All, int snapshotEvery = 1)
    {
        if (products == FeedProducts.None)
            throw new ArgumentException(
                "a feed carrying no products publishes nothing, which is a channel that should " +
                "not have been created rather than one that is quiet", nameof(products));

        if (snapshotEvery <= 0)
            throw new ArgumentOutOfRangeException(nameof(snapshotEvery), snapshotEvery,
                "a feed that skips every tick has no snapshot stream - leave the group's " +
                "snapshot interval unset instead, which says so");

        Symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
        _products = products;
        _snapshotEvery = snapshotEvery;

        _incremental = new (FeedProducts Product, Func<MarketEvent, MarketDataEvent?> Project)[]
        {
            (FeedProducts.Status, StatusOf),
            (FeedProducts.Trades, TradeOf),
            (FeedProducts.ByPrice, LevelsOf),
            (FeedProducts.ByOrder, OrdersOf),
            (FeedProducts.Indicative, IndicativeOf)
        };

        _snapshot = new (FeedProducts Product, Func<BookSnapshot, MarketDataEvent> Project)[]
        {
            (FeedProducts.Status, StatusImage),
            (FeedProducts.ByPrice, LevelsImage),
            (FeedProducts.ByOrder, OrdersImage),
            (FeedProducts.Indicative, IndicativeImage)
        };
    }

    public string Symbol { get; }

    public FeedProducts Products => _products;

    public int SnapshotEvery => _snapshotEvery;

    private bool Carries(FeedProducts product) => (_products & product) != 0;

    public IReadOnlyList<MarketDataEvent> Process(IReadOnlyList<OrderBookEvent> bookEvents)
    {
        var events = PublicHalf(bookEvents);
        if (events.Count == 0)
            return Array.Empty<MarketDataEvent>();

        List<MarketDataEvent>? output = null;

        foreach (var (product, project) in _incremental)
        {
            if (!Carries(product))
                continue;

            foreach (var ev in events)
            {
                if (project(ev) is { } data)
                    (output ??= new List<MarketDataEvent>()).Add(data);
            }
        }

        return output ?? (IReadOnlyList<MarketDataEvent>) Array.Empty<MarketDataEvent>();
    }

    public IReadOnlyList<MarketDataEvent> Snapshot(IReadOnlyList<OrderBookEvent> bookEvents)
    {
        var images = Images(bookEvents);
        if (images.Count == 0)
            return Array.Empty<MarketDataEvent>();

        if (++_snapshotTicks % _snapshotEvery != 0)
            return Array.Empty<MarketDataEvent>();

        List<MarketDataEvent>? output = null;

        foreach (var (product, project) in _snapshot)
        {
            if (!Carries(product))
                continue;

            foreach (var image in images)
                (output ??= new List<MarketDataEvent>()).Add(project(image));
        }

        return output ?? (IReadOnlyList<MarketDataEvent>) Array.Empty<MarketDataEvent>();
    }

    private static MarketDataEvent? StatusOf(MarketEvent ev) => ev switch
    {
        StatusChanged status => new InstrumentStatusDataEvent(status.Symbol, status.Time,
            status.Status, status.Reason, status.ResumesAt, status.LimitState),

        LimitStateChanged limit => new InstrumentStatusDataEvent(limit.Symbol, limit.Time,
            limit.Status, limit.Reason, limit.ResumesAt, limit.Side),

        _ => null
    };

    private static MarketDataEvent? TradeOf(MarketEvent ev) =>
        ev is TradePrinted trade
            ? new TradeDataEvent(trade.Symbol, trade.Time, trade.TradeId, trade.Price, trade.Quantity)
            : null;

    private static MarketDataEvent? LevelsOf(MarketEvent ev)
    {
        if (ev is not LevelsChanged levels)
            return null;

        var changes = new List<MarketByPriceDelta>(levels.Changes.Count);
        foreach (var change in levels.Changes)
        {
            changes.Add(new MarketByPriceDelta(change.Side, change.LevelIndex, change.Price,
                change.Quantity, change.Count, change.Action));
        }

        return new MarketByPriceDeltaEvent(levels.Symbol, levels.Time, levels.Depth, changes);
    }

    private static MarketDataEvent? OrdersOf(MarketEvent ev)
    {
        if (ev is not OrdersChanged orders)
            return null;

        var changes = new List<MarketByOrderDelta>(orders.Changes.Count);
        foreach (var change in orders.Changes)
        {
            changes.Add(new MarketByOrderDelta(change.Side, change.ExchangeOrderId, change.Price,
                change.Quantity, change.Action, change.TradeId));
        }

        return new MarketByOrderDeltaEvent(orders.Symbol, orders.Time, changes);
    }

    private static MarketDataEvent? IndicativeOf(MarketEvent ev) =>
        ev is IndicativePriceChanged changed
            ? new IndicativePriceDataEvent(changed.Symbol, changed.Time, changed.Price, changed.Quantity)
            : null;

    private static MarketDataEvent StatusImage(BookSnapshot snapshot) =>
        new InstrumentStatusDataEvent(snapshot.Symbol, snapshot.Time, snapshot.Status,
            snapshot.StatusReason, snapshot.ResumesAt, snapshot.LimitState);

    private static MarketDataEvent LevelsImage(BookSnapshot snapshot) =>
        new LevelsDataEvent(snapshot.Symbol, snapshot.Time, OrderBook.PublishedDepth,
            snapshot.Bids, snapshot.Offers);

    private static MarketDataEvent OrdersImage(BookSnapshot snapshot) =>
        new OrdersDataEvent(snapshot.Symbol, snapshot.Time, snapshot.Orders);

    private static MarketDataEvent IndicativeImage(BookSnapshot snapshot) =>
        new IndicativePriceDataEvent(snapshot.Symbol, snapshot.Time, snapshot.IndicativePrice,
            snapshot.IndicativeQuantity);

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

    private static IReadOnlyList<BookSnapshot> Images(IReadOnlyList<OrderBookEvent> bookEvents)
    {
        List<BookSnapshot>? images = null;

        for (var i = 0; i < bookEvents.Count; i++)
        {
            if (bookEvents[i] is BookSnapshot image)
                (images ??= new List<BookSnapshot>(1)).Add(image);
        }

        return images ?? (IReadOnlyList<BookSnapshot>) Array.Empty<BookSnapshot>();
    }
}
