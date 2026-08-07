namespace Circus.Sessions;

// A day's trading hours as a pure function of time: given an instant, what the schedule does
// next. Stateless, so one instance serves every book trading the same hours. A caller asks
// rather than being told - the opposite shape to a walker that holds state and reports the
// boundaries it has passed. A queue in front of several books needs the asking shape: it holds
// one pending transition per book and wants the next one, not a catch-up.
//
// The session list describes one day, repeated, and a day here is an anchor date rather than a
// calendar day: sessions carry offsets from their anchor's midnight, so a session may close on
// the day after the one it began on. What a day may not do is reach further than that, which is
// what keeps this a repeating description and what bounds how far back a query has to look.
//
// Holidays and half-days are not modelled. The nullable return on NextAfter is where they would
// show up.
public sealed class MarketSchedule
{
    private static readonly TimeSpan Day = TimeSpan.FromDays(1);

    private readonly IReadOnlyList<TradingSession> _sessions;

    // A single continuous session, the common case.
    public MarketSchedule(TimeSpan preOpen, TimeSpan open, TimeSpan close)
        : this(new[] {new TradingSession(preOpen, open, close)})
    {
    }

    public MarketSchedule(IReadOnlyList<TradingSession> sessions)
    {
        if (sessions.Count == 0) throw new ArgumentException("at least one session is required");

        for (var i = 0; i < sessions.Count; i++)
        {
            var session = sessions[i];

            // Strictly ordered, within a session and between neighbours alike. Two boundaries on
            // one instant cannot both be reached by a query keyed on time - the first is returned
            // and asking on from it steps over the second, so a session would open without
            // pre-opening, or begin without the one before it having closed. Venues that run
            // almost around the clock still stop, so requiring a gap costs nothing real and turns
            // an answer that would quietly be wrong into an exception where the schedule is
            // written.
            if (session.PreOpen >= session.Open) throw new ArgumentException("pre-open must be before open");
            if (session.Open >= session.Close) throw new ArgumentException("open must be before close");

            if (i > 0 && session.PreOpen <= sessions[i - 1].Close)
                throw new ArgumentException("sessions must be ordered and must not overlap or touch");
        }

        // The first session begins on the anchor day itself. Without this an anchor would name no
        // particular day, and the one-day lookback below would not be enough to find a session in
        // progress.
        if (sessions[0].PreOpen < TimeSpan.Zero || sessions[0].PreOpen >= Day)
            throw new ArgumentException("the first session must pre-open within its own day");

        // A day repeated is only a description of anything if one day's sessions end before the
        // next day's begin. Strictly under 24 hours rather than at most, for the same reason
        // neighbours may not touch: the day's last close and tomorrow's first pre-open are
        // neighbours too.
        if (sessions[sessions.Count - 1].Close - sessions[0].PreOpen >= Day)
            throw new ArgumentException("a day's sessions must span less than 24 hours");

        _sessions = sessions;
    }

    // What the schedule does next after `time`, or null when it has nothing left to do - which
    // this schedule never has, a day being repeated indefinitely. Nullable all the same, so a
    // caller is written against a schedule that can end: a calendar carrying holidays, or a
    // contract past its last trading day, is the same question with a finite answer.
    //
    // Strictly after `time`, so a caller standing on a boundary it has just handled is told the
    // one that follows rather than handed the same one back. Every boundary is reachable that
    // way, because the constructor refuses a schedule that would put two of them on one instant.
    public ScheduledTransition? NextAfter(DateTime time)
    {
        // Yesterday's anchor first, because that is the only place a session in progress can be
        // if there is one: sessions do not overlap, so one still running from yesterday means
        // none of today's has begun. Nothing earlier can reach here - a day spans under 24 hours
        // and starts within its anchor, so the day before yesterday closed before today began.
        for (var dayOffset = -1; dayOffset <= 0; dayOffset++)
        {
            var anchor = time.Date.AddDays(dayOffset);
            if (SessionAt(anchor, time) is not { } index) continue;

            // Inside a session: whichever of that session's own remaining boundaries comes next.
            var session = _sessions[index];
            return time < anchor.Add(session.Open)
                ? new ScheduledTransition(anchor.Add(session.Open), OrderBookStatus.Open,
                    TradeDateOn(anchor, index))
                : new ScheduledTransition(anchor.Add(session.Close), OrderBookStatus.Closed,
                    TradeDateOn(anchor, index), EndsTradingDay(index));
        }

        // Outside every session: the next one to pre-open, on today's anchor or tomorrow's.
        for (var dayOffset = 0; dayOffset <= 1; dayOffset++)
        {
            var anchor = time.Date.AddDays(dayOffset);
            for (var i = 0; i < _sessions.Count; i++)
            {
                var preOpen = anchor.Add(_sessions[i].PreOpen);
                if (preOpen > time)
                    return new ScheduledTransition(preOpen, OrderBookStatus.PreOpen, TradeDateOn(anchor, i));
            }
        }

        // Unreachable: tomorrow's anchor is midnight tonight at the earliest, which is ahead of
        // any instant today, so the loop above always finds something.
        return null;
    }

    // Only the day's last session ends the trading day; closing for a break leaves Day orders
    // resting for the session still to come.
    private bool EndsTradingDay(int sessionIndex) => sessionIndex == _sessions.Count - 1;

    // The day a session's business belongs to. An evening session that trades for tomorrow says
    // so on itself, so this is the anchor date and nothing about where the boundary happens to
    // land on a calendar.
    private DateOnly TradeDateOn(DateTime anchor, int sessionIndex) =>
        DateOnly.FromDateTime(anchor.AddDays(_sessions[sessionIndex].TradeDateOffset));

    // The session anchored on `anchor` that `time` falls inside, if any. Half-open, so a
    // session's close is not part of it.
    private int? SessionAt(DateTime anchor, DateTime time)
    {
        for (var i = 0; i < _sessions.Count; i++)
        {
            if (time >= anchor.Add(_sessions[i].PreOpen) && time < anchor.Add(_sessions[i].Close))
                return i;
        }

        return null;
    }
}
