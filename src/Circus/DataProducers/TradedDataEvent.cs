namespace Circus.DataProducers;

public record TradedDataEvent(DateTime Time, decimal Price, int Quantity);
