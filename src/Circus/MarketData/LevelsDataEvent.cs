namespace Circus.MarketData;

public record LevelsDataEvent(Security Security, DateTime Time, IReadOnlyList<Level> Bids,
        IReadOnlyList<Level> Offers)
    : MarketDataEvent(Security, Time);
