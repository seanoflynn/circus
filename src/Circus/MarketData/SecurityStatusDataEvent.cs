namespace Circus.MarketData;

// ResumesAt is when a timed interruption is due back, null when nothing is pending. LimitState
// is which way a daily limit has the market stuck - Buy for limit up, where buyers cannot push
// higher - and null when it is free to trade.
public record SecurityStatusDataEvent(string Symbol, DateTime Time, OrderBookStatus Status,
        StatusChangeReason Reason, DateTime? ResumesAt, Side? LimitState)
    : MarketDataEvent(Symbol, Time);
