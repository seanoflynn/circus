namespace Circus.Sessions;

// One session's boundaries, as offsets from the start of the day it is anchored on. A day's
// schedule is an ordered, non-overlapping list of these - a single continuous session for most
// products, two or more for one that breaks (a lunch recess, a separate evening session).
//
// Offsets rather than times of day, so a session may run past midnight: an offset at or beyond 24
// hours falls on the day after its anchor. A product pre-opening at 16:45, opening at 17:00 and
// closing at 16:00 the following afternoon is `(16:45, 17:00, 40:00)`. MarketSchedule keeps a
// day's whole span under 24 hours, so a session reaches at most into the day after its anchor.
//
// TradeDateOffset is the day the session's business belongs to, counted from its anchor. An
// evening session that is the next day's trading carries 1; everything else carries 0, which is
// why a schedule that stays within its day says nothing about it. This is the day an order good
// till a date is measured against, and it is not the date on the wall clock: every instant of an
// overnight session before midnight is dated a day behind the trading day it belongs to.
public record TradingSession(TimeSpan PreOpen, TimeSpan Open, TimeSpan Close, int TradeDateOffset = 0);
