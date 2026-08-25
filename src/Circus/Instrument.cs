namespace Circus;

public record Instrument(
    string Symbol,
    decimal TickSize,
    int MarketOrderProtectionTicks = 10,
    IReadOnlyList<PriceRestriction>? PriceRestrictions = null,
    MatchingAlgorithm MatchingAlgorithm = Circus.MatchingAlgorithm.PriceTime
);