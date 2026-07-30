namespace Circus.Sessions;

// One boundary a schedule has coming: when it falls, and the status it moves the book to.
//
// EndsTradingDay carries the same meaning it has on SessionStatusChangedArgs and on CloseTrading:
// false for a session closing with another still to come the same day, so Day orders rest across
// a lunch break. True (the default) for every other status, which ends nothing.
public readonly record struct ScheduledTransition(DateTime Time, OrderBookStatus Status,
    bool EndsTradingDay = true);
