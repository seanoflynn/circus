using Circus.Events;

namespace Circus.MarketData;

// Everything a venue publishes about one instrument, assembled in one place. A book's events go
// in, the messages a subscriber would receive come out.
//
// One bundle per instrument, and nothing in it remembers the book. Every product is a pure
// function of the events it is handed, since the book reports what moved - which price levels,
// which orders, and what state the instrument is in - rather than leaving a producer to work it
// out by shadowing the book. The only thing counted here is the snapshot cadence, which is this
// feed's own business rather than something the book knows about.
//
// A feed is never handed the book itself, only its events. Everything a consumer knows is derived
// from the stream rather than queried back out of the thing that produced it, which is what lets
// market data be rebuilt from a journal of those events with no books involved at all - the same
// property that makes a recorded trace replayable into a fresh venue.
//
// So each product is a row in one of the two tables below rather than a class of its own. They
// were classes while one of them still held state and the rest looked like it might: nine of them,
// each an interface implementation wrapping a loop over the same event list, and six of those
// differed from each other only in which event they matched and which record they built. What is
// left is the matching and the building, which is all that was ever there.
//
// Which products it carries is configured, because a venue is not one shape. CME channels carry
// by-price and by-order together with trades and status; Eurex splits by-order onto EOBI and
// by-price onto EMDI, publishing state on both; an ITCH-shaped venue carries by-order alone. All
// of them are this class with different flags, which is the point of the flags existing.
//
// Everything by default: a caller who has not thought about channels sees the whole venue, which
// is more than any real depth feed carries and the useful answer for a simulator.
//
// Two numbers go with the products. Depth is how far the by-price products run, which has to be
// agreed with the book rather than applied here - a shallower delta stream is not a filtered
// deeper one, so the book is asked to report at this depth and this feed takes those reports. The
// snapshot half does truncate, because an image cuts cleanly where a delta does not.
//
// snapshotEvery is how many snapshot ticks pass between images. The venue ticks at the finest
// cadence any of its channels wants and each feed counts the ticks it cares about, so a group
// whose depth feed restates itself every cycle and whose order-by-order feed restates itself every
// fifth is one interval and two counters rather than two schedules to keep in step. Real venues
// are shaped that way for the same reason: a full order-by-order image is the heaviest message a
// venue sends, so it cycles slower than the depth image beside it.
public sealed class InstrumentFeed
{
    // One row per product a venue publishes as it changes: the flag that turns it on, and how one
    // of the book's events becomes one of a subscriber's messages. Null from a projection means
    // this event is not that product's business, which is most events for most products.
    //
    // The order is the order the output comes out in, and it is fixed here rather than left to
    // fall out of how the events arrived - see Process.
    private readonly (FeedProducts Product, Func<MarketEvent, MarketDataEvent?> Project)[] _incremental;

    // The other half of what a venue publishes, on its own stream: where the book is, rather than
    // what changed about it. Each row republishes the same message type its incremental
    // counterpart does, so a subscriber applies a snapshot the same way it applies an update - CME
    // draws it that way too, the same fields on a different feed.
    //
    // Typed on BookSnapshot rather than on MarketEvent because every one of these answers that one
    // event, which is what lets a dispatch be searched for it once rather than once per product.
    //
    // That it is an event at all is the point. A snapshot is a statement of current state, and the
    // temptation is to build one by reading the book - but a snapshot produced that way leaves no
    // trace in the event stream and so cannot be reproduced by replaying it, which costs more than
    // it saves. Instead a snapshot tick dispatches an action like any other, the book answers with
    // an event carrying the image, and these read that event like every other product reads its
    // own. So there is no second mechanism here for snapshots, only a second table.
    private readonly (FeedProducts Product, Func<BookSnapshot, MarketDataEvent> Project)[] _snapshot;

    private readonly FeedProducts _products;
    private readonly int _depth;
    private readonly int _snapshotEvery;

    // Snapshot ticks seen, not dispatches: a tick is a dispatch carrying a BookSnapshot for this
    // instrument, and everything else passes without moving the count.
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

    // What this feed carries. A channel is a subset of the venue in two directions - which
    // instruments, and which products about them - and this is the second.
    public FeedProducts Products => _products;

    // How deep its by-price products run. The book has to be reporting at this depth for them to
    // carry anything, which is why the two are configured together.
    public int Depth => _depth;

    // How many snapshot ticks pass between images on this feed. One restates on every tick.
    public int SnapshotEvery => _snapshotEvery;

    private bool Carries(FeedProducts product) => (_products & product) != 0;

    // Ordering within one call is by product, in the table's order, rather than interleaved by
    // time: every event in a single dispatch shares an instant, so there is no time order among
    // them to preserve. Across calls it is the order the venue dispatched them in, which is the
    // ordering that actually carries meaning.
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

    // The snapshot half, kept a separate call rather than a second return value because the two
    // streams are numbered separately and published independently - a subscriber in sync reads
    // only the incremental one, and a channel is free to carry no snapshot feed at all.
    //
    // A dispatch that is not a snapshot tick produces nothing here, so the common path costs one
    // scan that finds nothing rather than a branch the caller has to know to take.
    //
    // Ticks are counted before the cadence is applied, so a feed on every fifth tick publishes on
    // the fifth and not the first. A joiner therefore waits at most a full cycle, which is what
    // the cycle means; publishing immediately and then every fifth would make the first gap
    // shorter than the promise.
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

    // What state the instrument is in, as one thing - CME's Security Status message, Eurex's
    // instrument state. The book publishes the parts separately because they are separate: a
    // status change and a limit lock are different events, and a limit-locked market is open. A
    // subscriber wanting to render "what is happening to this instrument" wants them together,
    // and assembling them is what this does.
    //
    // Assembling used to mean remembering. Each event carried one part of the composite and a
    // producer held the rest between messages, which made this the last thing here that could
    // drift from the book and the only one a missed message left permanently wrong. Both events
    // carry the whole of it now - the book was already holding all four fields, and BookSnapshot
    // was already publishing them as one composite - so this is a projection like every other
    // product, and a gap costs a subscriber the update rather than the truth.
    //
    // Which of the two events it came from is not preserved, and should not be: what a status
    // product says is where the instrument is, and both events answer that completely. That they
    // stay separate types upstream is what lets a consumer who cares about only one of them say
    // so - see LimitStateChanged.
    //
    // One message per contributing event, carrying that event's own time rather than a time chosen
    // for a batch. Each is a complete picture, so a consumer never needs two.
    private static MarketDataEvent? StatusOf(MarketEvent ev) => ev switch
    {
        StatusChanged status => new InstrumentStatusDataEvent(status.Symbol, status.Time,
            status.Status, status.Reason, status.ResumesAt, status.LimitState),

        // Side is which way a limit has the market stuck; the rest is the status it stays in while
        // stuck, which this event carries precisely so that it need not be held.
        LimitStateChanged limit => new InstrumentStatusDataEvent(limit.Symbol, limit.Time,
            limit.Status, limit.Reason, limit.ResumesAt, limit.Side),

        _ => null
    };

    // The public print, and nothing more than a translation of the one the book publishes.
    //
    // It used to derive the print itself, pairing the two FillOrderConfirmed events of a trade by
    // the id they share and taking the first of each pair. That worked, but a fill belongs to the
    // participant whose order filled and carries their CompanyId - so deriving a broadcast message
    // from it meant reading something no subscriber is entitled to. The book publishes TradePrinted
    // for the same pair now, and the pairing lives where the trade happened.
    //
    // The trade's id comes across with it. It is the only field of a fill that is not about who
    // filled - the two sides of a trade share it, and so do the order events the by-order product
    // publishes for them - so carrying it broadcasts nothing private and is what lets a subscriber
    // holding both products join a print to the fills that made it.
    private static MarketDataEvent? TradeOf(MarketEvent ev) =>
        ev is TradePrinted trade
            ? new TradeDataEvent(trade.Symbol, trade.Time, trade.TradeId, trade.Price, trade.Quantity)
            : null;

    // Aggregated depth, and a translation rather than a derivation: the book already worked out
    // which levels moved and reported them as one update, because its price ladders carry the
    // running totals. There is no second book to keep in step here and no state to lose. A product
    // deriving depth from order events would have to hold the book the subscriber is missing, which
    // is what the old LevelDataProducer did - and why it could never resync after a missed event.
    //
    // Depth is a subscription rather than a truncation. The book is asked to report at this depth
    // and this takes those reports; it does not take a deeper report and cut it down, because a
    // shallower window's departures are not present in a deeper window's report at all - see
    // LevelsChanged. A feed whose depth the book does not report publishes nothing, which is the
    // failure InstrumentGroup exists to prevent by building both from one number.
    private MarketDataEvent? LevelsOf(MarketEvent ev)
    {
        if (ev is not LevelsChanged levels || levels.Depth != _depth)
            return null;

        var changes = new List<MarketByPriceDelta>(levels.Changes.Count);
        foreach (var change in levels.Changes)
        {
            changes.Add(new MarketByPriceDelta(change.Side, change.LevelIndex, change.Price,
                change.Quantity, change.Count, change.Action));
        }

        return new MarketByPriceDeltaEvent(levels.Symbol, levels.Time, levels.Depth, changes);
    }

    // Order by order, and a translation for the reason the by-price product is one: the book
    // already worked out what changed about the displayed book and reported it as one update.
    //
    // It used to derive that itself, from the private order confirmations - including the rule that
    // an update losing time priority is the old id leaving and a new one arriving rather than a
    // modify in place. That rule is about a requeue the book performed, reconstructed a step later
    // by reading PreviousExchangeOrderId off a confirmation addressed to someone else. It lives in
    // the book now, for the reason the depth derivation moved there: the book is where the queue
    // actually moved, and reading a participant's confirmations is reading something no subscriber
    // is entitled to see.
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

    // The auction quote a book is running - CME's indicative opening price, Eurex's indicative
    // auction price. The book emits IndicativePriceChanged only when the quote moves, so there is
    // nothing to deduplicate against here.
    //
    // A null Price withdraws the quote (the book stopped crossing, or the auction ended), which a
    // subscriber must publish as such rather than leaving the last price standing.
    private static MarketDataEvent? IndicativeOf(MarketEvent ev) =>
        ev is IndicativePriceChanged changed
            ? new IndicativePriceDataEvent(changed.Symbol, changed.Time, changed.Price, changed.Quantity)
            : null;

    // The composite a joiner cannot rebuild: a status change and a limit lock arrive as separate
    // events at separate times. The incremental half carries the whole composite on either event,
    // which serves anyone who heard one of them; this serves someone who heard nothing at all, and
    // is the only thing that can.
    private static MarketDataEvent StatusImage(BookSnapshot snapshot) =>
        new InstrumentStatusDataEvent(snapshot.Symbol, snapshot.Time, snapshot.Status,
            snapshot.StatusReason, snapshot.ResumesAt, snapshot.LimitState);

    // The one place a feed shallower than its book truncates rather than subscribing. An image says
    // where the book is, so the first five entries of a ten-deep image are the five-deep image and
    // nothing is lost by cutting. A delta is the opposite - see LevelsChanged - which is why the
    // incremental half takes reports made at its depth instead.
    private MarketDataEvent LevelsImage(BookSnapshot snapshot) =>
        new LevelsDataEvent(snapshot.Symbol, snapshot.Time, _depth,
            Truncate(snapshot.Bids), Truncate(snapshot.Offers));

    // Every resting order, for a subscriber joining or recovering an order-by-order book. The
    // heaviest message this venue publishes, and the reason a real snapshot feed cycles slowly.
    private static MarketDataEvent OrdersImage(BookSnapshot snapshot) =>
        new OrdersDataEvent(snapshot.Symbol, snapshot.Time, snapshot.Orders);

    // Published on the cycle even when there is no quote, so a null price restates that the book is
    // not crossing rather than leaving a joiner to assume it. The incremental half says the same
    // thing by emitting a withdrawal, which is a message a late subscriber never heard.
    private static MarketDataEvent IndicativeImage(BookSnapshot snapshot) =>
        new IndicativePriceDataEvent(snapshot.Symbol, snapshot.Time, snapshot.IndicativePrice,
            snapshot.IndicativeQuantity);

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

    // The boundary where a book's output becomes a venue's. What a participant is told about its
    // own order stops here: the projections above are typed to MarketEvent, so this is the only
    // place the two halves are told apart, and it is a filter rather than a redaction because the
    // public events are their own events rather than copies with fields removed.
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

    // The images in a dispatch, which are what the snapshot half answers. Counting these as ticks
    // rather than counting every dispatch is what makes the cadence "every fifth snapshot" instead
    // of "every fifth action", which would tie how often a feed restates itself to how busy the
    // instrument is.
    //
    // A BookSnapshot is a MarketEvent, so searching the book's whole output for one finds exactly
    // what searching its public half would - which is why this does not run PublicHalf first.
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
