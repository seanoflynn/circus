namespace Circus.Sessions;

public record TradingSession(TimeSpan PreOpen, TimeSpan Open, TimeSpan Close, int TradeDateOffset = 0);
