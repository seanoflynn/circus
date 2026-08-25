namespace Circus.MarketData;

public record InstrumentStatusDataEvent(string Symbol, DateTime Time, OrderBookStatus Status,
        OrderBookStatusChangeReason Reason, DateTime? ResumesAt, Side? LimitState)
    : MarketDataEvent(Symbol, Time);
