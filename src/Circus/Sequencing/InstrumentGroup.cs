using Circus.Actions;
using Circus.MarketData;
using Circus.Sessions;

namespace Circus.Sequencing;

// A group of instruments that share one sequencer and the channels that publish them. Registering
// an instrument here adds it to both, so the wiring between them cannot be wrong.
//
// A product complex -- a spread and its legs -- needs a common dispatch order the moment implied
// pricing exists, and it needs its messages numbered on a stream a subscriber can count. This is
// the unit that provides both.
//
// One sequencer, several channels. That is the shape both venues have: the dispatch order is the
// venue's and there is one of it, while what gets published about it is a product decision with
// several answers. CME runs a channel per product group carrying by-price and by-order together;
// Eurex publishes the same instrument on EOBI and on EMDI with different products on each. A
// channel here differs from its siblings in what it carries, not in the order it sees things:
// which instruments, which products about them, how deep its by-price products run, and how often
// it restates itself. All four are declared on the channel, and the books are built to match -
// which they have to be for depth, since a shallow delta stream has to be diffed at its own
// window rather than cut down from a deep one.
//
// Declare the channels, then add instruments; an instrument goes on every channel unless it names
// some, because in both venues the channels of a group carry the same instruments and differ in
// products. A group that never declares one gets a single default channel carrying everything,
// which is what a caller who has not thought about channels wants.
//
// Single-threaded, like the sequencer and the channels inside it.
public sealed class InstrumentGroup
{
    private readonly Sequencer _sequencer;

    // What one channel publishes: the stream itself and the three things a feed on it is built
    // from. Held rather than derived, because a channel declared before any instrument has to be
    // able to describe itself, and one declared after has to build the same feed for a symbol that
    // is already here.
    private sealed record ChannelConfig(MarketDataChannel Channel, FeedProducts Products, int Depth,
        int SnapshotEvery);

    // Insertion-ordered, so a caller iterating them gets the order it declared them in rather than
    // a dictionary's.
    private readonly Dictionary<string, ChannelConfig> _channels = new();
    private readonly List<string> _channelOrder = new();
    private readonly List<string> _symbols = new();

    // The books this group built, so a channel declared later can ask them to start reporting at
    // its depth. Only the ones built here: a book handed in ready-made reports what its owner told
    // it to, and this is not the place to change that.
    private readonly Dictionary<string, OrderBook> _ownBooks = new();

    // snapshotInterval is how often each book is asked to restate itself. Set it to the finest
    // cadence any channel here wants; a channel wanting a slower one counts ticks and skips, which
    // is what AddChannel's snapshotEvery is for. Null publishes no snapshot feed at all, which
    // leaves a subscriber unable to join mid-session or recover from a gap - the position
    // everything here was in before there was one.
    public InstrumentGroup(DateTime start, TimeSpan? snapshotInterval = null)
    {
        _sequencer = new Sequencer(start, snapshotInterval);
    }

    public Sequencer Sequencer => _sequencer;

    public IReadOnlyList<string> Symbols => _symbols;

    public IReadOnlyList<string> ChannelNames => _channelOrder;

    // The one channel, for a venue that has one. Refused rather than guessed at once there are
    // several: which of them a caller meant is not something to pick for them, and the messages
    // are numbered per channel so merging two would produce a stream nobody publishes.
    public MarketDataChannel Channel => _channelOrder.Count switch
    {
        1 => _channels[_channelOrder[0]].Channel,
        0 => throw new InvalidOperationException(
            "this group has no channels yet - declare one, or add an instrument and take the " +
            "default that comes with it"),
        _ => throw new InvalidOperationException(
            $"this group publishes {_channelOrder.Count} channels ({string.Join(", ", _channelOrder)}), " +
            "so there is no single one to take - name the one you mean")
    };

    public MarketDataChannel ChannelNamed(string name) =>
        _channels.TryGetValue(name, out var config)
            ? config.Channel
            : throw new ArgumentException($"no channel named {name} in this group", nameof(name));

    // Declares a channel and what it publishes. Every instrument already registered joins it, so
    // the order channels and instruments are declared in does not change what comes out.
    //
    // depth is how far its by-price products run. The channel's books are told to report at it -
    // the ones this group built, at least - because a shallower delta stream is not a filtered
    // deeper one and has to be diffed at its own window. Ten by default, which is what CME's
    // futures books carry; one is a top-of-book product, and a channel carrying no by-price
    // product ignores it.
    //
    // snapshotEvery is how many of the group's snapshot ticks pass between this channel's images.
    // One restates on every tick. It is a count rather than an interval so that the channels of a
    // group stay in step: the group ticks at the finest cadence any of them wants, and a channel
    // wanting a slower one skips ticks rather than keeping a schedule of its own.
    public void AddChannel(string name, FeedProducts products = FeedProducts.All,
        int depth = OrderBook.DefaultPublishedDepth, int snapshotEvery = 1)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (_channels.ContainsKey(name))
            throw new ArgumentException($"a channel named {name} is already in this group", nameof(name));

        // Here rather than left to the first feed built from it, so a channel declared before any
        // instrument is refused where it was written rather than when something is added to it.
        if (depth <= 0)
            throw new ArgumentOutOfRangeException(nameof(depth), depth,
                "a channel carrying no levels is not a by-price channel");

        if (snapshotEvery <= 0)
            throw new ArgumentOutOfRangeException(nameof(snapshotEvery), snapshotEvery,
                "a channel that skips every tick has no snapshot stream - leave the group's " +
                "snapshot interval unset instead, which says so");

        var channel = new MarketDataChannel(name);
        var config = new ChannelConfig(channel, products, depth, snapshotEvery);
        _channels[name] = config;
        _channelOrder.Add(name);

        foreach (var symbol in _symbols)
        {
            // Before the feed, so the feed it joins is one the book will actually report to.
            if (Carries(config, FeedProducts.ByPrice) && _ownBooks.TryGetValue(symbol, out var book))
                book.AlsoReport(depth);

            channel.Add(FeedFor(symbol, config));
        }
    }

    // Registers an instrument: creates a bare OrderBook and an InstrumentFeed, adds the book and
    // schedule to the sequencer and the feed to the channel.
    //
    // How deep the book reports is not a parameter here any more, because it is not a property of
    // the instrument: it is what its by-price channels publish, and the book is built to report at
    // each of those depths. A group whose only channel is ten deep gets a ten-deep book; one
    // running a top-of-book channel beside it gets a book reporting at one and at ten, since a
    // one-deep delta stream has to be diffed at one deep rather than cut down from ten.
    //
    // A caller wanting a book configured further - custom price restrictions, its own depths -
    // builds one and uses the overload below.
    public void Add(Instrument instrument, MarketSchedule schedule,
        IReadOnlyList<string>? channels = null)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        ArgumentNullException.ThrowIfNull(schedule);

        RequireChannelsExist(channels);
        EnsureSomeChannel(channels);

        var book = new OrderBook(instrument, DepthsFor(channels));
        _sequencer.Add(book, schedule);
        _ownBooks[instrument.Symbol] = book;
        Publish(instrument.Symbol, channels);
    }

    // Registers a pre-built book (e.g. with custom price restrictions) alongside its schedule and
    // an instrument feed for it.
    //
    // Its depths are its owner's. A channel here publishes by-price data for it only where the two
    // agree, so a book built at ten and a channel declared at five leave that channel's by-price
    // products empty for this instrument - build the book with the depths its channels publish.
    public void Add(IOrderBook book, MarketSchedule schedule, IReadOnlyList<string>? channels = null)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(schedule);

        RequireChannelsExist(channels);

        _sequencer.Add(book, schedule);
        Publish(book.Symbol, channels);
    }

    // The depths the by-price channels carrying this instrument publish at. Channels carrying no
    // by-price product contribute nothing, so an order-by-order channel does not make the book
    // diff a window nobody reads; if none of them do, the book still reports at the default, which
    // costs one bounded diff and keeps a book's behaviour independent of who happens to subscribe.
    private int[] DepthsFor(IReadOnlyList<string>? channels)
    {
        var depths = (channels ?? _channelOrder)
            .Select(name => _channels[name])
            .Where(config => Carries(config, FeedProducts.ByPrice))
            .Select(config => config.Depth)
            .Distinct()
            .ToArray();

        return depths.Length > 0 ? depths : new[] {OrderBook.DefaultPublishedDepth};
    }

    // The depths the book this group built for a symbol reports at, or nothing for a book handed
    // in ready-made. Internal because it is the one part of the wiring between a channel's
    // declared depth and its book's reports that is otherwise invisible: get it wrong and the
    // symptom is by-price data quietly not arriving, which is what this lets a test rule out.
    internal IReadOnlyList<int> PublishedDepthsFor(string symbol) =>
        _ownBooks.TryGetValue(symbol, out var book)
            ? book.PublishedDepths
            : Array.Empty<int>();

    private static bool Carries(ChannelConfig config, FeedProducts product) =>
        (config.Products & product) != 0;

    private static InstrumentFeed FeedFor(string symbol, ChannelConfig config) =>
        new(symbol, config.Products, config.Depth, config.SnapshotEvery);

    // The default channel, created before the book is, so the book can be built to report at the
    // depth that channel publishes. Only when the caller named none - see Publish.
    private void EnsureSomeChannel(IReadOnlyList<string>? channels)
    {
        if (channels == null && _channels.Count == 0)
            AddChannel(MarketDataChannel.DefaultName);
    }

    // Before the book reaches the sequencer, so naming a channel that does not exist leaves the
    // group exactly as it was rather than holding a book nothing publishes.
    private void RequireChannelsExist(IReadOnlyList<string>? channels)
    {
        if (channels == null) return;

        foreach (var name in channels)
        {
            if (!_channels.ContainsKey(name))
                throw new ArgumentException($"no channel named {name} in this group", nameof(channels));
        }
    }

    // Puts a symbol on the channels that carry it, and records it so a channel declared later
    // picks it up too.
    //
    // Naming none means all of them, which is what both venues do - the channels of a group carry
    // the same instruments and differ in products. Naming some is for a venue that does not, and
    // naming one that does not exist is refused rather than silently publishing nowhere.
    private void Publish(string symbol, IReadOnlyList<string>? channels)
    {
        // Only when the caller named none. Someone asking for a channel by name has a venue shape
        // in mind, and inventing a default underneath them would publish where they did not ask.
        // Already done for an instrument this group built the book for, since the book had to be
        // told what to report before it existed; idempotent, so the other overload lands here.
        EnsureSomeChannel(channels);

        var carrying = channels ?? _channelOrder;

        foreach (var name in carrying)
            _channels[name].Channel.Add(FeedFor(symbol, _channels[name]));

        _symbols.Add(symbol);
    }

    // Submits an action to the group's sequencer.
    public void Submit(OrderBookAction action) => _sequencer.Submit(action);
}