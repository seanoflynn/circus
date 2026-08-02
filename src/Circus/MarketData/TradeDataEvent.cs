namespace Circus.MarketData;

public record TradeDataEvent(string Symbol, DateTime Time, decimal Price, int Quantity)
    : MarketDataEvent(Symbol, Time);
