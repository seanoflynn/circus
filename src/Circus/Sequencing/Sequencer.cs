using Circus.Actions;
using Circus.Events;
using Circus.Sessions;

namespace Circus.Sequencing;

// One queue in front of the books, and the only component that knows more than one instrument
// exists. Its dispatch order is the venue's order of events.
//
// That is the whole reason it exists. A book must never be handed an action stamped behind the
// last one it saw - it throws if that happens - so exactly one component may decide what reaches
// a book first, and it is this one. Nothing else should hold a book and drive it directly while a
// sequencer has it.
//
// Three sources feed the one queue. Client flow arrives already stamped, through Submit. Schedule
// transitions come from asking each book's MarketSchedule what is next. Interruption ticks come
// from the books' own events - a book saying when its pause is due back becomes a poke queued at
// that instant, which is the only feedback in the system.
//
// Ordering is by (Time, Kind, Sequence), and per-book monotonicity is not enforced separately: it
// follows from dispatching in global time order, which is the point of having one queue rather
// than one per book. Every queued action carries a unique submission counter, so no two entries
// ever compare equal and dispatch order is a function of the inputs alone - no dictionary
// iteration, no arrival races once submitted. That is what makes the stream reproducible.
//
// Single-threaded by construction: one queue, one dispatch loop, one thread. The shape invites a
// lock and a lock is the wrong answer - a gateway submitting from an I/O thread wants a handoff
// queue into this thread, not contention on it.
//
// No clock of its own. AdvanceTo is the seam that makes live and replay the same code: a replay
// submits a trace and advances to the end, a live pump advances to whatever its clock reads on a
// tick. For the same reason the books registered here should be bare OrderBooks - a
// TimestampingOrderBook would stamp its own clock reading over the instant the sequencer decided
// this action happened at.
public sealed class Sequencer
{
    // Priority is a plain tuple rather than a type of its own: it is compared, never handled, and
    // ValueTuple already compares its members in order.
    private readonly PriorityQueue<OrderBookAction, (DateTime Time, DispatchKind Kind, long Counter)>
        _queue = new();

    // Keyed on the symbol rather than on the record. A symbol is what identifies a
    // contract at a venue, and it is the part an action arriving from anywhere else can be
    // trusted to carry: two Instrument records describing the same contract need not be equal,
    // since the restriction list on them compares by reference. A routing table that turned that
    // into "no book is registered for GCZ6" while holding a book for GCZ6 would be a bad
    // afternoon.
    private readonly Dictionary<string, (IOrderBook Book, MarketSchedule Schedule)> _books = new();

    // Never reused, so it is what makes every queue entry distinct and every tie decidable.
    private long _counter;

    private long _sequence;
    private DateTime _now;

    // How often each book is asked to describe itself, or null for a venue that publishes no
    // snapshot feed. Logical, never wall-clock: a replay reproduces the snapshots along with
    // everything else because the ticks come from the same queue as every other action.
    private readonly TimeSpan? _snapshotInterval;

    public Sequencer(DateTime start, TimeSpan? snapshotInterval = null)
    {
        if (snapshotInterval is { } interval && interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(snapshotInterval), interval,
                "a snapshot cycle must move forward");

        _now = start;
        _snapshotInterval = snapshotInterval;
    }

    // Everything at or before this instant has been dispatched, so nothing may be inserted there
    // any more. Starts wherever the venue was told it was starting - a replay at the beginning of
    // its trace, a live pump at its clock.
    public DateTime LogicalNow => _now;

    // Registers a book and the schedule driving it, keyed on the book's own symbol, and queues
    // its first boundary - the next one strictly after logical now.
    //
    // A book registered mid-session is not caught up. The schedule is asked what is next, never
    // what was missed, so a book added between its open and its close stays as it is until that
    // close arrives. Register before the day begins, or submit the transitions to put the book
    // where it should be: that decision belongs to whoever starts the venue, not here.
    public void Add(IOrderBook book, MarketSchedule schedule)
    {
        if (!_books.TryAdd(book.Symbol, (book, schedule)))
            throw new ArgumentException(
                $"a book is already registered for {book.Symbol}", nameof(book));

        QueueNextTransition(book.Symbol, schedule, _now);
        QueueNextSnapshot(book.Symbol, _now);
    }

    // Queues client flow at its own stamp.
    //
    // Refused if stamped behind logical now: the past has already been dispatched and cannot be
    // inserted into. Refused for an instrument with no book registered, because a routing mistake is
    // worth hearing about where it was made rather than as a lookup failure mid-dispatch.
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

    // Dispatches everything queued at or before `time`, in order, then holds logical now there.
    //
    // Dispatching can queue more work at or before that same instant - the boundary after a
    // schedule transition, a poke for an interruption that just began - so the loop drains rather
    // than taking a snapshot of what was queued on entry.
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

            // Logical now moves with the action being dispatched rather than jumping to the
            // caller's target, so anything that action queues is placed against the instant it
            // actually happened at.
            _now = next.Time;

            var (book, schedule) = _books[action.Symbol];
            var events = book.Process(action);

            _sequence++;
            dispatched.Add(new Dispatched(_sequence, action, events));

            // The next boundary is queued only once this one has been dispatched, so the queue
            // holds a single pending transition per book however far ahead the schedule runs.
            if (next.Kind == DispatchKind.ScheduleTransition)
                QueueNextTransition(action.Symbol, schedule, next.Time);

            // Queued one ahead, like a schedule boundary, so the queue holds a single pending tick
            // per book however long the run.
            if (next.Kind == DispatchKind.SnapshotTick)
                QueueNextSnapshot(action.Symbol, next.Time);

            QueueInterruptionTicks(events, next.Time);
        }

        _now = time;
        return dispatched;
    }

    // A book pausing says when it is due back, and that becomes a poke at the deadline. Nothing is
    // ever cancelled: a close landing before the deadline clears the book's own resume time, and
    // the tick that arrives afterwards finds nothing to do. An interruption that extends emits a
    // fresh deadline, which queues a fresh tick and leaves the earlier one inert. Self-correcting,
    // which is what makes punctuality this cheap - no interruption epoch on the action, no
    // cancellation bookkeeping.
    //
    // The book's own resume time stays the authority throughout. This only pokes it on time.
    private void QueueInterruptionTicks(IReadOnlyList<OrderBookEvent> events, DateTime dispatchedAt)
    {
        // Indexed rather than events.OfType<StatusChanged>(): a StatusChanged is present in a
        // small fraction of dispatches, so this avoids an iterator allocation to find one that
        // usually is not there.
        for (var i = 0; i < events.Count; i++)
        {
            if (events[i] is not StatusChanged status)
                continue;

            // Strictly ahead: a deadline that has already arrived was served by the action that
            // set it, and a poke queued at the instant being dispatched would only spin.
            if (status.ResumesAt is { } resumesAt && resumesAt > dispatchedAt)
                Enqueue(new AdvanceTime {Symbol = status.Symbol, Time = resumesAt},
                    DispatchKind.InterruptionTick);
        }
    }

    // Every book on its own interval from where it was registered, all of them due together on a
    // shared start. A real venue rotates instead, spreading its instruments across the cycle to
    // flatten the bandwidth of a feed nobody in sync is reading - which is a wire concern this has
    // no equivalent of, and rotating here would only make the cycle harder to reason about.
    private void QueueNextSnapshot(string symbol, DateTime after)
    {
        if (_snapshotInterval is not { } interval) return;

        Enqueue(new PublishSnapshot {Symbol = symbol, Time = after + interval},
            DispatchKind.SnapshotTick);
    }

    private void QueueNextTransition(string symbol, MarketSchedule schedule, DateTime after)
    {
        // A schedule with nothing left to say - a holiday calendar past its end - simply stops
        // driving its book. Whoever registered it decides what that means.
        if (schedule.NextAfter(after) is not { } transition) return;

        Enqueue(ToAction(symbol, transition), DispatchKind.ScheduleTransition);
    }

    private void Enqueue(OrderBookAction action, DispatchKind kind)
    {
        _counter++;
        _queue.Enqueue(action, (action.Time, kind, _counter));
    }

    // No reference price on an opening this builds: where that number comes from is a decision
    // kept outside the engine, and it reaches a book as an ordinary submitted action rather than
    // through a lookup wired into the schedule.
    private static OrderBookAction ToAction(string symbol, ScheduledTransition transition) =>
        transition.Status switch
        {
            OrderBookStatus.PreOpen => new PreOpenTrading {Symbol = symbol, Time = transition.Time},
            OrderBookStatus.Open => new OpenTrading {Symbol = symbol, Time = transition.Time},
            OrderBookStatus.Closed => new CloseTrading
            {
                Symbol = symbol, Time = transition.Time, EndsTradingDay = transition.EndsTradingDay
            },
            _ => throw new ArgumentOutOfRangeException(nameof(transition), transition.Status,
                "a schedule moves a book between pre-open, open and closed, and nowhere else")
        };
}