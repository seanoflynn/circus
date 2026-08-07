namespace Circus.Sessions;

// One boundary a schedule has coming: when it falls, which trading day it belongs to, and the
// status it moves the book to.
//
// TradeDate is the day the venue says it is trading over this boundary, which is the session's
// anchor date shifted by its TradeDateOffset. It is not read off Time: an overnight session's
// pre-open and open fall on the calendar day before the one they trade for.
//
// EndsTradingDay carries the same meaning it has on CloseTrading: false for a session closing
// with another still to come the same day, so Day orders rest across a lunch break. True (the
// default) for every other status, which ends nothing.
public readonly record struct ScheduledTransition(DateTime Time, OrderBookStatus Status, DateOnly TradeDate,
    bool EndsTradingDay = true);
