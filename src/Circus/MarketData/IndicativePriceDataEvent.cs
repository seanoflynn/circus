namespace Circus.MarketData;

public record IndicativePriceDataEvent(string Symbol, DateTime Time, decimal? Price, int Quantity)
    : MarketDataEvent(Symbol, Time);
