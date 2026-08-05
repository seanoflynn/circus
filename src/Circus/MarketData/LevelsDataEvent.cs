namespace Circus.MarketData;

// The by-price snapshot: where the book is, rather than what changed about it.
//
// Depth is the window this feed publishes, and the lists are that window or shorter when the book
// holds less. Unlike a delta an image truncates cleanly - the first five entries of a ten-deep
// image are the five-deep image - so a feed shallower than the book it reads takes the top of what
// it is given rather than needing an image of its own.
public record LevelsDataEvent(string Symbol, DateTime Time, int Depth, IReadOnlyList<Level> Bids,
        IReadOnlyList<Level> Offers)
    : MarketDataEvent(Symbol, Time);
