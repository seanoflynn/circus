namespace Circus.MarketData;

public record IndicativePriceDataEvent(DateTime Time, decimal? Price, int Quantity);
