using Circus.Actions;
using Circus.MarketData;
using Circus.Sessions;

namespace Circus.Sequencing;

// A group of instruments that share a single sequencer and a single market data channel.
// Registering an instrument here adds it to both, so the wiring between them cannot be wrong.
//
// A product complex -- a spread and its legs -- needs a common dispatch order the moment implied
// pricing exists, and it needs a single channel so the per-instrument messages carry contiguous
// sequence numbers a subscriber can count. This is the unit that provides both, bundled together.
//
// Single-threaded, like the sequencer and the channel inside it.
public sealed class InstrumentGroup
{
    private readonly Sequencer _sequencer;
    private readonly MarketDataChannel _channel;
    private readonly List<string> _symbols = new();

    // snapshotInterval is how often each book restates itself on the channel's snapshot stream.
    // Null publishes no snapshot feed, which leaves a subscriber unable to join mid-session or
    // recover from a gap - the position everything here was in before there was one.
    public InstrumentGroup(DateTime start, TimeSpan? snapshotInterval = null)
    {
        _sequencer = new Sequencer(start, snapshotInterval);
        _channel = new MarketDataChannel();
    }

    public Sequencer Sequencer => _sequencer;
    public MarketDataChannel Channel => _channel;
    public IReadOnlyList<string> Symbols => _symbols;

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
        FeedProducts products = FeedProducts.All)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        ArgumentNullException.ThrowIfNull(schedule);

        var book = new OrderBook(instrument, publishedDepth);
        _sequencer.Add(book, schedule);
        _channel.Add(new InstrumentFeed(instrument.Symbol, products));
        _symbols.Add(instrument.Symbol);
    }

    // Registers a pre-built book (e.g. with custom price restrictions) alongside its schedule and
    // an instrument feed for it.
    public void Add(IOrderBook book, MarketSchedule schedule,
        FeedProducts products = FeedProducts.All)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(schedule);

        _sequencer.Add(book, schedule);
        _channel.Add(new InstrumentFeed(book.Symbol, products));
        _symbols.Add(book.Symbol);
    }

    // Submits an action to the group's sequencer.
    public void Submit(OrderBookAction action) => _sequencer.Submit(action);
}