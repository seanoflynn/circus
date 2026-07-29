namespace Circus.DataProducers;

public record IndicativePriceDataEvent(DateTime Time, decimal? Price, int Quantity);
