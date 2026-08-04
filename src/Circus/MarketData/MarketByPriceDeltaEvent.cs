namespace Circus.MarketData;

// One aggregated price level changing - CME's Market by Price, Eurex's EMDI. The incremental half
// of the by-price feed; the periodic full image is the snapshot half.
//
// Quantity is displayed size, never remaining: an iceberg's hidden reserve is not on the book.
// Quantity and Count are both zero on Removed.
//
// Keyed on Price rather than on LevelIndex, so each of these is idempotent and can be applied on
// its own - see LevelChanged, which carries the same reasoning for the book event behind it.
// LevelIndex is the rank the level holds, or on Removed the rank it last held, and is there for a
// consumer that wants it rather than for identifying the level.
//
// The published window is ten deep, so a level pushed past that is Removed whether or not orders
// still rest there. It returns as Added if it comes back.
public record MarketByPriceDeltaEvent(string Symbol, DateTime Time, Side Side, int LevelIndex,
        decimal Price, int Quantity, int Count, MarketByPriceDeltaAction Action)
    : MarketDataEvent(Symbol, Time);
