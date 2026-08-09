namespace Circus.MarketData;

// The by-price snapshot: where the book is, rather than what changed about it.
//
// Depth is the window this feed publishes, and the lists are that window or shorter when the book
// holds less. Always OrderBook.PublishedDepth, matching the delta stream beside it.
//
// An image would truncate cleanly where a delta does not - the first five entries of a ten-deep
// image are the five-deep image - but it is not cut here, because a subscriber holding a
// five-deep image and a ten-deep delta stream is holding two different books.
public record LevelsDataEvent(string Symbol, DateTime Time, int Depth, IReadOnlyList<Level> Bids,
        IReadOnlyList<Level> Offers)
    : MarketDataEvent(Symbol, Time);
