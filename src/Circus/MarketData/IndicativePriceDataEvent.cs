namespace Circus.MarketData;

public record IndicativePriceDataEvent(Security Security, DateTime Time, decimal? Price, int Quantity)
    : MarketDataEvent(Security, Time);
