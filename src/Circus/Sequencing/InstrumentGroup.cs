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

    public InstrumentGroup(DateTime start)
    {
        _sequencer = new Sequencer(start);
        _channel = new MarketDataChannel();
    }

    public Sequencer Sequencer => _sequencer;
    public MarketDataChannel Channel => _channel;
    public IReadOnlyList<string> Symbols => _symbols;

    // Registers an instrument: creates a bare OrderBook and an InstrumentFeed, adds the book and
    // schedule to the sequencer and the feed to the channel. Depth is not a parameter - every
    // by-price product is ten deep, fixed in the book that reports the levels.
    public void Add(Instrument instrument, MarketSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        ArgumentNullException.ThrowIfNull(schedule);

        var book = new OrderBook(instrument);
        _sequencer.Add(book, schedule);
        _channel.Add(new InstrumentFeed(instrument.Symbol));
        _symbols.Add(instrument.Symbol);
    }

    // Registers a pre-built book (e.g. with custom price restrictions) alongside its schedule and
    // an instrument feed for it.
    public void Add(IOrderBook book, MarketSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(schedule);

        _sequencer.Add(book, schedule);
        _channel.Add(new InstrumentFeed(book.Symbol));
        _symbols.Add(book.Symbol);
    }

    // Submits an action to the group's sequencer.
    public void Submit(OrderBookAction action) => _sequencer.Submit(action);
}