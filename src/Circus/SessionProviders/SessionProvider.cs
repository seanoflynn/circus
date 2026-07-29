using Circus.OrderBook;

namespace Circus.SessionProviders;

// Drives an order book's status off a clock, given a day's schedule. Update() is pushed the
// current time and fires whatever boundaries have been passed since it was last called, so a
// caller that goes quiet for a day catches up rather than replaying every boundary it missed.
public class SessionProvider : ISessionProvider
{
    private readonly IReadOnlyList<TradingSession> _sessions;
    public event EventHandler<SessionStatusChangedArgs>? Changed;

    private OrderBookStatus? _status;

    // The session the current (or upcoming) PreOpen/Open/Closed run belongs to. Resolved when
    // leaving Closed, then reused for that session's open and close.
    private int _sessionIndex;
    private int _nextSessionIndex;

    private DateTime _nextTime;
    private OrderBookStatus _nextStatus;
    private bool _nextEndsTradingDay = true;

    // A single continuous session, the common case.
    public SessionProvider(TimeSpan preOpenTime, TimeSpan openTime, TimeSpan closeTime)
        : this(new[] {new TradingSession(preOpenTime, openTime, closeTime)})
    {
    }

    public SessionProvider(IReadOnlyList<TradingSession> sessions)
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

    public void Update(DateTime time)
    {
        if (_status == null)
        {
            _status = OrderBookStatus.Closed;
            Changed?.Invoke(this, new SessionStatusChangedArgs(_status.Value, time));
            SetNextTime(time);
        }

        while (time >= _nextTime)
        {
            _status = _nextStatus;
            if (_nextStatus == OrderBookStatus.PreOpen)
                _sessionIndex = _nextSessionIndex;

            Changed?.Invoke(this, new SessionStatusChangedArgs(_status.Value, _nextTime, _nextEndsTradingDay));
            SetNextTime(time);
        }
    }

    // Every boundary is anchored on the caller's own date rather than walked forward from the
    // last one, which is what lets an idle day be skipped instead of replayed.
    private void SetNextTime(DateTime time)
    {
        _nextEndsTradingDay = true;

        switch (_status)
        {
            case OrderBookStatus.Closed:
            {
                var (index, dayOffset) = ResolveNextSession(time.TimeOfDay);
                _nextSessionIndex = index;
                _nextStatus = OrderBookStatus.PreOpen;
                _nextTime = time.Date.AddDays(dayOffset).Add(_sessions[index].PreOpen);
                break;
            }
            case OrderBookStatus.PreOpen:
                _nextStatus = OrderBookStatus.Open;
                _nextTime = time.Date.Add(_sessions[_sessionIndex].Open);
                break;
            default:
                _nextStatus = OrderBookStatus.Closed;
                _nextTime = time.Date.Add(_sessions[_sessionIndex].Close);

                // Only the day's last session ends the trading day; closing for a break leaves
                // Day orders resting for the session still to come.
                _nextEndsTradingDay = _sessionIndex == _sessions.Count - 1;
                break;
        }
    }

    // Which session to head for from Closed, and whether it falls on the next day. A session
    // already in progress wins, so a provider started (or woken) mid-session catches up into
    // it rather than waiting for the next one.
    private (int Index, int DayOffset) ResolveNextSession(TimeSpan timeOfDay)
    {
        for (var i = 0; i < _sessions.Count; i++)
        {
            if (timeOfDay >= _sessions[i].PreOpen && timeOfDay < _sessions[i].Close)
                return (i, 0);
        }

        for (var i = 0; i < _sessions.Count; i++)
        {
            if (_sessions[i].PreOpen > timeOfDay)
                return (i, 0);
        }

        // Past the last close of the day - start again with tomorrow's first session.
        return (0, 1);
    }
}
