namespace Circus.MarketData;

public abstract record MarketDataEvent(string Symbol, DateTime Time);