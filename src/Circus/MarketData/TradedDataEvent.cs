namespace Circus.MarketData;

public record TradedDataEvent(string Symbol, DateTime Time, decimal Price, int Quantity)
    : MarketDataEvent(Symbol, Time);
