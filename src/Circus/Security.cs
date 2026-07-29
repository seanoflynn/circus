using Circus.OrderBook.Restrictions;

namespace Circus;

// MarketOrderProtectionTicks is not a price restriction and stays here: it prices a market
// order rather than restricting one, deciding how far through the book an order with no limit
// of its own may sweep.
public record Security(
    string Name,
    SecurityType Type,
    decimal TickSize,
    decimal TickValue,
    int MarketOrderProtectionTicks = 10,
    IReadOnlyList<PriceRestrictionConfig>? PriceRestrictions = null
);
