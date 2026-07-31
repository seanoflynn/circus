using Circus.Actions;
using Circus.Events;
using Circus.MarketData;
using Circus.Time;

namespace Circus.Tests.Helpers;

// An OrderBook plus the level view a subscriber would keep beside it. The book
// answers no questions about its own levels, so a test that needs to know where orders ended
// up rebuilds them from the event stream - which also means these assertions are against what
// a market data consumer can actually see, not the book's internals.
internal sealed class LevelTrackingOrderBook : IOrderBook
{
    // Deeper than any test asks for, so a level is only ever missing because the caller's own
    // maxPrices cut it off.
    private const int MaxLevels = 100;

    private readonly IOrderBook _book;
    private readonly LevelDataProducer _levelDataProducer = new(MaxLevels);
    private LevelsDataEvent _levels;

    public LevelTrackingOrderBook(Instrument instrument, IClock clock)
    {
        _book = new TimestampingOrderBook(instrument, clock);
        _levels = new LevelsDataEvent(instrument.Symbol, default, Array.Empty<Level>(), Array.Empty<Level>());
    }

    public string Symbol => _book.Symbol;

    public OrderBookStatus Status => _book.Status;

    public IReadOnlyList<OrderBookEvent> Process(OrderBookAction action)
    {
        var events = _book.Process(action);

        foreach (var levels in _levelDataProducer.Process(events))
            _levels = levels;

        return events;
    }

    public IReadOnlyList<Level> GetLevels(Side side, int maxPrices) =>
        (side == Side.Buy ? _levels.Bids : _levels.Offers).Take(maxPrices).ToList();
}