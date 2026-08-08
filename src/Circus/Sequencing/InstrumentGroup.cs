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
// which instruments, which products about them, and how often it restates itself. All three are
// declared on the channel.
//
// How deep its by-price products run is not among them. Every book publishes one window, and a
// subscriber wanting fewer levels than that holds it and shows what it likes - see
// OrderBook.PublishedDepth for why a shallower stream cannot be handed to it instead.
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
    private sealed record ChannelConfig(MarketDataChannel Channel, FeedProducts Products, int SnapshotEvery);

    // Insertion-ordered, so a caller iterating them gets the order it declared them in rather than
    // a dictionary's.
    private readonly Dictionary<string, ChannelConfig> _channels = new();
    private readonly List<string> _channelOrder = new();
    private readonly List<string> _symbols = new();

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
    // snapshotEvery is how many of the group's snapshot ticks pass between this channel's images.
    // One restates on every tick. It is a count rather than an interval so that the channels of a
    // group stay in step: the group ticks at the finest cadence any of them wants, and a channel
    // wanting a slower one skips ticks rather than keeping a schedule of its own.
    public void AddChannel(string name, FeedProducts products = FeedProducts.All, int snapshotEvery = 1)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (_channels.ContainsKey(name))
            throw new ArgumentException($"a channel named {name} is already in this group", nameof(name));

        if (snapshotEvery <= 0)
            throw new ArgumentOutOfRangeException(nameof(snapshotEvery), snapshotEvery,
                "a channel that skips every tick has no snapshot stream - leave the group's " +
                "snapshot interval unset instead, which says so");

        var channel = new MarketDataChannel(name);
        var config = new ChannelConfig(channel, products, snapshotEvery);
        _channels[name] = config;
        _channelOrder.Add(name);

        foreach (var symbol in _symbols)
            channel.Add(FeedFor(symbol, config));
    }

    // Registers an instrument: creates a bare OrderBook and an InstrumentFeed, adds the book and
    // schedule to the sequencer and the feed to the channel.
    //
    // A caller wanting a book configured further - custom price restrictions, say - builds one and
    // uses the overload below. Nothing about depth needs arranging between the two: every book
    // publishes the same window.
    public void Add(Instrument instrument, MarketSchedule schedule,
        IReadOnlyList<string>? channels = null)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        ArgumentNullException.ThrowIfNull(schedule);

        RequireChannelsExist(channels);
        EnsureSomeChannel(channels);

        _sequencer.Add(new OrderBook(instrument), schedule);
        Publish(instrument.Symbol, channels);
    }

    // Registers a pre-built book (e.g. with custom price restrictions) alongside its schedule and
    // an instrument feed for it. Nothing here has to agree with anything about the book beyond its
    // symbol, which is the point of a fixed window: a book built anywhere publishes what every
    // channel reads.
    public void Add(IOrderBook book, MarketSchedule schedule, IReadOnlyList<string>? channels = null)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(schedule);

        RequireChannelsExist(channels);

        _sequencer.Add(book, schedule);
        Publish(book.Symbol, channels);
    }

    private static InstrumentFeed FeedFor(string symbol, ChannelConfig config) =>
        new(symbol, config.Products, config.SnapshotEvery);

    // The default channel, for a caller who named none - see Publish.
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