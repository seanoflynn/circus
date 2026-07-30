namespace Circus.MarketData;

// ExchangeOrderId only - never CompanyId/ClientOrderId, which identify the originating
// client and must not be broadcast on a public depth feed.
public record OrderBookDeltaEvent(Security Security, DateTime Time, Side Side, string ExchangeOrderId,
        decimal Price, int Quantity, OrderBookDeltaAction Action)
    : MarketDataEvent(Security, Time);
