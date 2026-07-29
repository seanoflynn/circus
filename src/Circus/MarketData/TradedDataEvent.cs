namespace Circus.MarketData;

public record TradedDataEvent(DateTime Time, decimal Price, int Quantity);
