namespace Circus.Sessions;

// EndsTradingDay is meaningful only when Status is Closed: false for a session closing with
// another still to come the same day, so a consumer can forward it to the book and leave Day
// orders resting across the break. True (the default) for every other status, which ends nothing.
public record SessionStatusChangedArgs(OrderBookStatus Status, DateTime Time, bool EndsTradingDay = true);
