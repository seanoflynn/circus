namespace Circus;

// MarketOrderProtectionTicks is not a price restriction and stays here: it prices a market
// order rather than restricting one, deciding how far through the book an order with no limit
// of its own may sweep.
//
// MatchingAlgorithm is a plain property rather than one of the PriceRestriction records, for
// something like the reason those are a list: an instrument trades under any number of
// restrictions, each carrying a width of its own, but it allocates under exactly one algorithm
// and that choice has nothing to configure.
public record Instrument(
    string Symbol,
    decimal TickSize,
    int MarketOrderProtectionTicks = 10,
    IReadOnlyList<PriceRestriction>? PriceRestrictions = null,
    // Qualified because the parameter shares its name with its type, and a default value is one
    // of the places that reads as ambiguous.
    MatchingAlgorithm MatchingAlgorithm = Circus.MatchingAlgorithm.PriceTime
);