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
// channel here differs from its siblings in what it carries, not in the order it sees things.
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

    // Insertion-ordered, so a caller iterating them gets the order it declared them in rather than
    // a dictionary's.
    private readonly Dictionary<string, (MarketDataChannel Channel, FeedProducts Products)> _channels = new();
    private readonly List<string> _channelOrder = new();
    private readonly List<string> _symbols = new();

    // snapshotInterval is how often each book restates itself on the channel's snapshot stream.
    // Null publishes no snapshot feed, which leaves a subscriber unable to join mid-session or
    // recover from a gap - the position everything here was in before there was one.
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
        _channels.TryGetValue(name, out var entry)
            ? entry.Channel
            : throw new ArgumentException($"no channel named {name} in this group", nameof(name));

    // Declares a channel and what it publishes. Every instrument already registered joins it, so
    // the order channels and instruments are declared in does not change what comes out.
    public void AddChannel(string name, FeedProducts products = FeedProducts.All)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (_channels.ContainsKey(name))
            throw new ArgumentException($"a channel named {name} is already in this group", nameof(name));

        var channel = new MarketDataChannel(name);
        _channels[name] = (channel, products);
        _channelOrder.Add(name);

        foreach (var symbol in _symbols)
            channel.Add(new InstrumentFeed(symbol, products));
    }

    // Registers an instrument: creates a bare OrderBook and an InstrumentFeed, adds the book and
    // schedule to the sequencer and the feed to the channel.
    //
    // publishedDepth is how deep that book reports its levels - the deepest any product taken off
    // it will want, since a channel publishing less truncates what it is given and one publishing
    // more has nothing to truncate from. Ten by default, which is what CME's futures books carry.
    // A caller wanting a book configured further builds one and uses the overload below.
    // products is what the channel publishes about it. Everything by default, which is more than
    // a real feed carries and the useful answer until a caller has a venue shape in mind.
    public void Add(Instrument instrument, MarketSchedule schedule,
        int publishedDepth = OrderBook.DefaultPublishedDepth,
        IReadOnlyList<string>? channels = null)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        ArgumentNullException.ThrowIfNull(schedule);

        RequireChannelsExist(channels);

        var book = new OrderBook(instrument, publishedDepth);
        _sequencer.Add(book, schedule);
        Publish(instrument.Symbol, channels);
    }

    // Registers a pre-built book (e.g. with custom price restrictions) alongside its schedule and
    // an instrument feed for it.
    public void Add(IOrderBook book, MarketSchedule schedule, IReadOnlyList<string>? channels = null)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(schedule);

        RequireChannelsExist(channels);

        _sequencer.Add(book, schedule);
        Publish(book.Symbol, channels);
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
        if (channels == null && _channels.Count == 0)
            AddChannel(MarketDataChannel.DefaultName);

        var carrying = channels ?? _channelOrder;

        foreach (var name in carrying)
        {
            var (channel, products) = _channels[name];
            channel.Add(new InstrumentFeed(symbol, products));
        }

        _symbols.Add(symbol);
    }

    // Submits an action to the group's sequencer.
    public void Submit(OrderBookAction action) => _sequencer.Submit(action);
}