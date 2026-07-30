namespace Circus.Sessions;

// Drives an order book's status off a clock, given a day's schedule. Update() is pushed the
// current time and fires whatever boundaries have been passed since it was last called, so a
// caller that goes quiet for a day catches up rather than replaying every boundary it missed.
//
// The hours themselves live in MarketSchedule, which answers what is due without being walked.
// What is left here is the walk: the status this provider believes the book is in, the session
// that status belongs to, and the one boundary it is waiting on.
public class SessionProvider : ISessionProvider
{
    private readonly MarketSchedule _schedule;
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
        : this(new MarketSchedule(preOpenTime, openTime, closeTime))
    {
    }

    public SessionProvider(IReadOnlyList<TradingSession> sessions)
        : this(new MarketSchedule(sessions))
    {
    }

    public SessionProvider(MarketSchedule schedule)
    {
        _schedule = schedule;
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
    // last one, which is what lets an idle day be skipped instead of replayed. That anchoring is
    // why this asks the schedule which session to head for and reads the times off it, rather
    // than asking MarketSchedule.NextAfter what comes next: the boundary this fires can be one
    // the caller's clock has already passed.
    private void SetNextTime(DateTime time)
    {
        _nextEndsTradingDay = true;

        switch (_status)
        {
            case OrderBookStatus.Closed:
            {
                var (index, dayOffset) = _schedule.NextSessionAt(time.TimeOfDay);
                _nextSessionIndex = index;
                _nextStatus = OrderBookStatus.PreOpen;
                _nextTime = time.Date.AddDays(dayOffset).Add(_schedule.Sessions[index].PreOpen);
                break;
            }
            case OrderBookStatus.PreOpen:
                _nextStatus = OrderBookStatus.Open;
                _nextTime = time.Date.Add(_schedule.Sessions[_sessionIndex].Open);
                break;
            default:
                _nextStatus = OrderBookStatus.Closed;
                _nextTime = time.Date.Add(_schedule.Sessions[_sessionIndex].Close);
                _nextEndsTradingDay = _schedule.EndsTradingDay(_sessionIndex);
                break;
        }
    }
}
