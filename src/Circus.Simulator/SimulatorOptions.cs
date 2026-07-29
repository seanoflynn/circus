namespace Circus.Simulator;

// Weights are threshold cut-offs evaluated in order (cancel, then update, then market),
// not probabilities that must sum to 1 — whatever mass is left over produces a limit order.
public record SimulatorOptions(
    decimal StartingPrice = 1000m,
    int MinQuantity = 1,
    int MaxQuantity = 10,
    int PriceRangeTicks = 100,
    double CancelWeight = 0.2,
    double UpdateWeight = 0.1,
    double MarketOrderWeight = 0.05,
    double CrossProbability = 0.15
);
