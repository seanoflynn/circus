namespace Circus.Sessions;

public sealed class MarketSchedule
{
    private static readonly TimeSpan Day = TimeSpan.FromDays(1);

    private readonly IReadOnlyList<TradingSession> _sessions;

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

            // Strictly ordered, between neighbouring sessions as well as within one. Two boundaries on a
            // single instant cannot both be reached by a query keyed on time - the first is returned, and
            // asking on from it steps over the second.
            if (session.PreOpen >= session.Open) throw new ArgumentException("pre-open must be before open");
            if (session.Open >= session.Close) throw new ArgumentException("open must be before close");

            if (i > 0 && session.PreOpen <= sessions[i - 1].Close)
                throw new ArgumentException("sessions must be ordered and must not overlap or touch");
        }

        if (sessions[0].PreOpen < TimeSpan.Zero || sessions[0].PreOpen >= Day)
            throw new ArgumentException("the first session must pre-open within its own day");

        if (sessions[sessions.Count - 1].Close - sessions[0].PreOpen >= Day)
            throw new ArgumentException("a day's sessions must span less than 24 hours");

        _sessions = sessions;
    }

    public ScheduledTransition? NextAfter(DateTime time)
    {
        // Yesterday's anchor first, because that is the only place a session still in progress can be:
        // sessions do not overlap, so one running from yesterday means none of today's has begun.
        for (var dayOffset = -1; dayOffset <= 0; dayOffset++)
        {
            var anchor = time.Date.AddDays(dayOffset);
            if (SessionAt(anchor, time) is not { } index) continue;

            var session = _sessions[index];
            return time < anchor.Add(session.Open)
                ? new ScheduledTransition(anchor.Add(session.Open), OrderBookStatus.Open,
                    TradeDateOn(anchor, index))
                : new ScheduledTransition(anchor.Add(session.Close), OrderBookStatus.Closed,
                    TradeDateOn(anchor, index), EndsTradingDay(index));
        }

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

        return null;
    }

    private bool EndsTradingDay(int sessionIndex) => sessionIndex == _sessions.Count - 1;

    private DateOnly TradeDateOn(DateTime anchor, int sessionIndex) =>
        DateOnly.FromDateTime(anchor.AddDays(_sessions[sessionIndex].TradeDateOffset));

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
