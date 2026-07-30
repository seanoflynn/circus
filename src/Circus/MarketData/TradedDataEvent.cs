namespace Circus.MarketData;

public record TradedDataEvent(Security Security, DateTime Time, decimal Price, int Quantity)
    : MarketDataEvent(Security, Time);
