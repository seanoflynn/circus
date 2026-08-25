namespace Circus.Sessions;

public readonly record struct ScheduledTransition(DateTime Time, OrderBookStatus Status, DateOnly TradeDate,
    bool EndsTradingDay = true);
