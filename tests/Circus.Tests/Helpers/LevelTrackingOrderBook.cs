using Circus.Actions;
using Circus.Events;
using Circus.MarketData;
using Circus.Time;

namespace Circus.Tests.Helpers;

// An OrderBook plus the level view a test needs to see where orders ended up.
//
// The book keeps that aggregate itself now - the price ladders carry displayed size and order
// count per level - so this asks it rather than rebuilding one from the event stream. What the
// book reports as LevelsChanged is the same aggregate, and BookLevelViewTests holds the two
// against each other; a test that only wants to know where an order rested reads it here.
internal sealed class LevelTrackingOrderBook : IOrderBook
{
    // Deeper than the ten a feed publishes, so a level is only ever missing from an assertion
    // because the caller's own maxPrices cut it off - a test about where orders rest should not
    // have to think about the publishing window.
    private const int MaxLevels = 100;

    private readonly OrderBook _inner;
    private readonly IOrderBook _book;

    public LevelTrackingOrderBook(Instrument instrument, IClock clock)
    {
        _inner = new OrderBook(instrument);
        _book = new TimestampingOrderBook(_inner, clock);
    }

    public string Symbol => _book.Symbol;

    public OrderBookStatus Status => _book.Status;

    public IReadOnlyList<OrderBookEvent> Process(OrderBookAction action) => _book.Process(action);

    public IReadOnlyList<Level> GetLevels(Side side, int maxPrices) =>
        _inner.GetLevels(side, Math.Min(maxPrices, MaxLevels));
}
