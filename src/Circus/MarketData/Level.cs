namespace Circus.MarketData;

// Aggregated market data, not a book structure: one publishable line per price.
public record Level(decimal Price, int Quantity, int Count);
