using Circus.Actions;
using Circus.Events;
using Circus.Sessions;

namespace Circus.Sequencing;

public sealed class Sequencer
{
    private readonly PriorityQueue<OrderBookAction, (DateTime Time, DispatchKind Kind, long Counter)>
        _queue = new();

    private readonly Dictionary<string, (IOrderBook Book, MarketSchedule Schedule)> _books = new();

    private long _counter;

    private long _sequence;
    private DateTime _now;

    private readonly TimeSpan? _snapshotInterval;

    public Sequencer(DateTime start, TimeSpan? snapshotInterval = null)
    {
        if (snapshotInterval is { } interval && interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(snapshotInterval), interval,
                "a snapshot cycle must move forward");

        _now = start;
        _snapshotInterval = snapshotInterval;
    }

    public DateTime LogicalNow => _now;

    public void Add(IOrderBook book, MarketSchedule schedule)
    {
        if (!_books.TryAdd(book.Symbol, (book, schedule)))
            throw new ArgumentException(
                $"a book is already registered for {book.Symbol}", nameof(book));

        QueueNextTransition(book.Symbol, schedule, _now);
        QueueNextSnapshot(book.Symbol, _now);
    }

    public void Submit(OrderBookAction action)
    {
        if (action.Time == default)
            throw new ArgumentException(
                $"{action.GetType().Name} has no Time. A sequencer orders actions by when they " +
                "happened, so an unstamped one has no place in the queue.", nameof(action));

        if (action.Time < _now)
            throw new ArgumentException(
                $"{action.GetType().Name} is stamped {action.Time:O}, behind logical now " +
                $"({_now:O}). Everything up to that instant has been dispatched already.",
                nameof(action));

        if (!_books.ContainsKey(action.Symbol))
            throw new ArgumentException(
                $"no book is registered for {action.Symbol}", nameof(action));

        Enqueue(action, DispatchKind.ClientFlow);
    }

    public IReadOnlyList<Dispatched> AdvanceTo(DateTime time)
    {
        if (time < _now)
            throw new ArgumentException(
                $"cannot advance to {time:O}, behind logical now ({_now:O}). Time runs one way.",
                nameof(time));

        var dispatched = new List<Dispatched>();

        while (_queue.TryPeek(out _, out var next) && next.Time <= time)
        {
            var action = _queue.Dequeue();

            _now = next.Time;

            var (book, schedule) = _books[action.Symbol];
            var events = book.Process(action);

            _sequence++;
            dispatched.Add(new Dispatched(_sequence, action, events));

            if (next.Kind == DispatchKind.ScheduleTransition)
                QueueNextTransition(action.Symbol, schedule, next.Time);

            if (next.Kind == DispatchKind.SnapshotTick)
                QueueNextSnapshot(action.Symbol, next.Time);

            QueueInterruptionTicks(events, next.Time);
        }

        _now = time;
        return dispatched;
    }

    private void QueueInterruptionTicks(IReadOnlyList<OrderBookEvent> events, DateTime dispatchedAt)
    {
        for (var i = 0; i < events.Count; i++)
        {
            if (events[i] is not StatusChanged status)
                continue;

            // Strictly ahead: a poke queued at the instant being dispatched would only spin.
            if (status.ResumesAt is { } resumesAt && resumesAt > dispatchedAt)
                Enqueue(new AdvanceTime {Symbol = status.Symbol, Time = resumesAt},
                    DispatchKind.InterruptionTick);
        }
    }

    private void QueueNextSnapshot(string symbol, DateTime after)
    {
        if (_snapshotInterval is not { } interval) return;

        Enqueue(new PublishSnapshot {Symbol = symbol, Time = after + interval},
            DispatchKind.SnapshotTick);
    }

    private void QueueNextTransition(string symbol, MarketSchedule schedule, DateTime after)
    {
        if (schedule.NextAfter(after) is not { } transition) return;

        Enqueue(ToAction(symbol, transition), DispatchKind.ScheduleTransition);
    }

    private void Enqueue(OrderBookAction action, DispatchKind kind)
    {
        _counter++;
        _queue.Enqueue(action, (action.Time, kind, _counter));
    }

    private static OrderBookAction ToAction(string symbol, ScheduledTransition transition) =>
        transition.Status switch
        {
            OrderBookStatus.PreOpen => new PreOpenTrading
            {
                Symbol = symbol, Time = transition.Time, TradeDate = transition.TradeDate
            },
            OrderBookStatus.Open => new OpenTrading
            {
                Symbol = symbol, Time = transition.Time, TradeDate = transition.TradeDate
            },
            OrderBookStatus.Closed => new CloseTrading
            {
                Symbol = symbol, Time = transition.Time, TradeDate = transition.TradeDate,
                EndsTradingDay = transition.EndsTradingDay
            },
            _ => throw new ArgumentOutOfRangeException(nameof(transition), transition.Status,
                "a schedule moves a book between pre-open, open and closed, and nowhere else")
        };
}