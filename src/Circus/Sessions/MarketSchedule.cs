namespace Circus.Sessions;

// A day's trading hours as a pure function of time: given an instant, what the schedule does
// next. Stateless, so one instance serves every book trading the same hours, and a caller asks
// rather than being told - the opposite shape to SessionProvider, which walks forward and reports
// the boundaries it has passed. A queue in front of several books needs the asking shape: it
// holds one pending transition per book and wants the next one, not a catch-up.
//
// The session list describes one day, repeated: past the day's last close the schedule rolls into
// tomorrow's first session. Holidays, half-days and a session spanning midnight are not modelled
// - TradingSession says why the last of those cannot be.
public sealed class MarketSchedule
{
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
            if (session.PreOpen > session.Open) throw new ArgumentException("pre-open must be before open");
            if (session.Open > session.Close) throw new ArgumentException("open must be before close");

            // Ordered and non-overlapping in one check: a session may begin the moment the
            // previous one closes, but not before.
            if (i > 0 && session.PreOpen < sessions[i - 1].Close)
                throw new ArgumentException("sessions must be ordered and must not overlap");
        }

        _sessions = sessions;
    }

    // What the schedule does next after `time`, or null when it has nothing left to do - which
    // this schedule never has, a day being repeated indefinitely. Nullable all the same, so a
    // caller is written against a schedule that can end: a calendar carrying holidays, or a
    // contract past its last trading day, is the same question with a finite answer.
    //
    // Strictly after `time`, so a caller standing on a boundary it has just handled is told the
    // one that follows rather than handed the same one back. The corollary is that two boundaries
    // sharing an instant - a session closing exactly as the next pre-opens, which the constructor
    // permits - cannot both be reached this way: the close is returned, and asking again from
    // there steps over the pre-open. SessionProvider drives such a day correctly because it walks
    // by status rather than by time. Anything iterating by time alone wants either distinct
    // instants or a query carrying where it left off, and which of those is right is a question
    // for whatever ends up consuming this.
    public ScheduledTransition? NextAfter(DateTime time)
    {
        var timeOfDay = time.TimeOfDay;

        // Inside a session: whichever of that session's own remaining boundaries comes next.
        var current = SessionAt(timeOfDay);
        if (current != null)
        {
            var session = _sessions[current.Value];
            return timeOfDay < session.Open
                ? new ScheduledTransition(time.Date.Add(session.Open), OrderBookStatus.Open)
                : new ScheduledTransition(time.Date.Add(session.Close), OrderBookStatus.Closed,
                    EndsTradingDay(current.Value));
        }

        // Outside every session: the next one pre-opens, today or tomorrow.
        var (index, dayOffset) = NextSessionAt(timeOfDay);
        return new ScheduledTransition(time.Date.AddDays(dayOffset).Add(_sessions[index].PreOpen),
            OrderBookStatus.PreOpen);
    }

    // The day's sessions, in order. Read by SessionProvider, which anchors boundaries on its
    // caller's own date rather than walking them from the last one.
    internal IReadOnlyList<TradingSession> Sessions => _sessions;

    // Which session to head for, and whether it falls on the next day. A session already in
    // progress wins, so a caller starting (or waking) mid-session catches up into it rather than
    // waiting for the next one.
    internal (int Index, int DayOffset) NextSessionAt(TimeSpan timeOfDay)
    {
        var current = SessionAt(timeOfDay);
        if (current != null) return (current.Value, 0);

        for (var i = 0; i < _sessions.Count; i++)
        {
            if (_sessions[i].PreOpen > timeOfDay)
                return (i, 0);
        }

        // Past the last close of the day - start again with tomorrow's first session.
        return (0, 1);
    }

    // Only the day's last session ends the trading day; closing for a break leaves Day orders
    // resting for the session still to come.
    internal bool EndsTradingDay(int sessionIndex) => sessionIndex == _sessions.Count - 1;

    // The session `timeOfDay` falls inside, if any. Half-open, so a session's close is not part
    // of it - which is what lets a close and the next pre-open share an instant.
    private int? SessionAt(TimeSpan timeOfDay)
    {
        for (var i = 0; i < _sessions.Count; i++)
        {
            if (timeOfDay >= _sessions[i].PreOpen && timeOfDay < _sessions[i].Close)
                return i;
        }

        return null;
    }
}
