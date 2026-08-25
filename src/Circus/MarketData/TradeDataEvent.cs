namespace Circus.MarketData;

public record TradeDataEvent(string Symbol, DateTime Time, string TradeId, decimal Price, int Quantity)
    : MarketDataEvent(Symbol, Time);
