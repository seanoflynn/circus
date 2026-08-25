namespace Circus.MarketData;

public record LevelsDataEvent(string Symbol, DateTime Time, int Depth, IReadOnlyList<Level> Bids,
        IReadOnlyList<Level> Offers)
    : MarketDataEvent(Symbol, Time);
