# Persistence, step 1: the seam and the in-memory journal

The implementation plan for the first step of `persistence-plan.md`. That document argues *where*
the journal goes and why; this one is what actually gets written, file by file, and what is
deliberately left until step 2.

## Scope

**In.** The `IJournal` seam, the three journal entries, an in-memory implementation, and the two
call sites that write to it - `Sequencer.AdvanceTo` for actions and `InstrumentGroup` for
configuration. Tests that pin the seam's behaviour, and a benchmark variant that prices it.

**Out.** Reading. Nothing in this step consumes a journal, and `IJournalReader` is not defined,
because what `Read(afterSequence)` should do with a config entry that falls after that sequence is a
recovery question and guessing at it now means writing an interface twice. `CheckpointTaken` waits
for step 3 for the same reason. No durable store, no serialisation, no new package references -
`Circus` stays dependency-free.

**The point of the step.** After it, a running venue writes down everything a recovery would need,
and one test demonstrates that the journal replays back to itself. Nothing recovers yet, but the
recording is real and the shape of what step 2 reads is settled by working code rather than by this
document.

## Files

```
src/Circus/Persistence/JournalEntry.cs      new
src/Circus/Persistence/IJournal.cs          new
src/Circus/Persistence/InMemoryJournal.cs   new
src/Circus/Sequencing/Sequencer.cs          + field, + ctor parameter, ~6 lines in AdvanceTo
src/Circus/Sequencing/InstrumentGroup.cs    + field, + ctor parameter, 3 edits
tests/Circus.Tests/Persistence/JournalTests.cs               new
benchmarks/Circus.Benchmarks/OrderBookThroughputBenchmarks.cs + one benchmark
docs/persistence-plan.md                    one line amended (see "Decisions settled")
```

Namespace `Circus.Persistence`, folder-per-namespace like `Sequencing/` and `MarketData/`. The
project added in step 4 takes the same namespace in a different assembly, so a caller reaching for a
file journal later adds a project reference and not a second `using`.

## The types

### `JournalEntry.cs`

```csharp
using Circus.Actions;
using Circus.Sessions;

namespace Circus.Persistence;

// What a venue writes down, and the whole of it.
//
// Input rather than output. A journal records the actions the venue dispatched, not the events
// they produced, because the events are a function of the actions and the books are not: an
// OrderBook reads no clock and consults nothing ambient, so re-dispatching the same actions into
// the same configuration reproduces the same events. Journalling events instead would record
// more, cost more, and still not rebuild a book.
public abstract record JournalEntry;

// The first entry in any journal: the instant the group's sequencer was started at, which is
// where a recovery has to start its own.
public sealed record VenueStarted(DateTime Start) : JournalEntry;

// An instrument and the schedule driving it. `At` is the group's logical now when it was
// registered, because Sequencer.Add queues the next boundary strictly after that instant - so an
// instrument added mid-session is a different thing from the same instrument added before the
// day began, and a recovery that replays it at the wrong moment gets a different first
// transition.
public sealed record InstrumentAdded(Instrument Instrument, MarketSchedule Schedule, DateTime At)
    : JournalEntry;

// One dispatched client action, at the venue's own dispatch count - the number already carried
// by Dispatched. Sequence numbers here are not contiguous: schedule transitions and interruption
// ticks consume them without being recorded, so a gap is a derived dispatch and never a lost
// entry.
public sealed record ActionDispatched(long Sequence, OrderBookAction Action) : JournalEntry;
```

### `IJournal.cs`

```csharp
namespace Circus.Persistence;

// Where a venue's record goes. Append-only, and single-threaded like everything it is called
// from: a sequencer dispatches on one thread and this is called from inside that loop, so an
// implementation needs no locking and must not call back into the venue.
public interface IJournal
{
    void Append(JournalEntry entry);

    // Called once per AdvanceTo that dispatched anything, after the loop and before the caller
    // gets the events back. That is the durability boundary: whatever a store has to do to make
    // the batch survive a crash, it does here, and it does it before anything caused by those
    // actions is published or confirmed.
    //
    // Nothing needs it yet - the in-memory journal's implementation is empty. It is on the
    // interface from the start so that adding a durable store in step 4 is a new class rather
    // than a change to the dispatch loop.
    void Flush();
}
```

### `InMemoryJournal.cs`

```csharp
namespace Circus.Persistence;

// Everything in a list. Not a placeholder for the real thing: it is what the tests and the
// deterministic samples use, and it is what makes "run a venue, replay its journal, compare"
// a unit test rather than a fixture with a temp directory.
//
// Unbounded, deliberately. A journal that discarded entries would not be one.
public sealed class InMemoryJournal : IJournal
{
    private readonly List<JournalEntry> _entries = new();

    public IReadOnlyList<JournalEntry> Entries => _entries;

    public void Append(JournalEntry entry) => _entries.Add(entry);

    // Already durable to the same degree the rest of the process is.
    public void Flush()
    {
    }
}
```

## The edits

### `Sequencer`

A field, an optional constructor parameter, and six lines in the loop.

```csharp
    // Null when nothing is recording, which is the default and what every existing caller gets.
    // Checked rather than dispatched through a do-nothing implementation: this is the dispatch
    // loop, the check is constant-false for an unjournalled venue and so perfectly predicted,
    // and an interface call that does nothing is not free.
    private readonly IJournal? _journal;

    public Sequencer(DateTime start, IJournal? journal = null)
    {
        _now = start;
        _journal = journal;
    }
```

and in `AdvanceTo`:

```csharp
            var action = _queue.Dequeue();
            _now = next.Time;

            var (book, schedule) = _books[action.Symbol];

            // Moved up from below the Process call so the number the journal records and the
            // number on the Dispatched record are the same number. No observable change - it is
            // a counter, and nothing reads it between here and there.
            _sequence++;

            // Written ahead of the book seeing it, so a journal is a record of what the venue
            // attempted. If Process throws - an unknown action type, a bug in matching - the
            // action that caused it is on the log, which is what a post-mortem needs and what
            // makes the crash reproducible. Writing behind Process instead would drop it and
            // leave a recovery quietly producing a state the venue never had.
            //
            // Client flow only. A schedule transition is a function of the schedule and an
            // interruption tick a function of an event a replay re-emits, so recording either
            // would have a recovery dispatch it twice: once regenerated, once read back.
            if (_journal is not null && next.Kind == DispatchKind.ClientFlow)
                _journal.Append(new ActionDispatched(_sequence, action));

            var events = book.Process(action);

            dispatched.Add(new Dispatched(_sequence, action, events));
```

and at the foot of the method:

```csharp
        _now = time;

        // Group commit, and the boundary the durability rule is stated against: the caller
        // publishes what this returns, so a flush here is a flush before anything these actions
        // caused is visible anywhere. Skipped when nothing dispatched, so a quiet tick costs
        // nothing.
        if (_journal is not null && dispatched.Count > 0)
            _journal.Flush();

        return dispatched;
```

The class comment gains a paragraph, because a reader arriving at `Sequencer` should be told why the
journal is here of all places:

> A journal, if one is attached, is written from inside the dispatch loop. It goes here rather than
> at `Submit` because submit order is not dispatch order - anything at or after logical now is
> accepted, so a caller may submit 12:05 before 12:01 - and a journal in submit order is not in time
> order, which is the one property a replay of it depends on.

### `InstrumentGroup`

```csharp
    // Held as well as handed to the sequencer, because the group writes the two entries that
    // describe the venue and the sequencer writes the one that describes what it did. It is also
    // what the Add overload below asks in order to refuse a book it cannot write down.
    private readonly IJournal? _journal;

    public InstrumentGroup(DateTime start, IJournal? journal = null)
    {
        _journal = journal;
        _sequencer = new Sequencer(start, journal);
        _channel = new MarketDataChannel();

        _journal?.Append(new VenueStarted(start));
    }
```

`Add(Instrument, MarketSchedule)` gains one line, at the end:

```csharp
    public void Add(Instrument instrument, MarketSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        ArgumentNullException.ThrowIfNull(schedule);

        var at = _sequencer.LogicalNow;

        var book = new OrderBook(instrument);
        _sequencer.Add(book, schedule);
        _channel.Add(new InstrumentFeed(instrument.Symbol));
        _symbols.Add(instrument.Symbol);

        // Written after the registration rather than ahead of it, unlike an action: this
        // validates synchronously and refuses a duplicate symbol by throwing, so a journal
        // written first would carry an instrument the venue does not have. The action path
        // writes ahead because there the failure mode is a crash rather than a refusal.
        _journal?.Append(new InstrumentAdded(instrument, schedule, at));
    }
```

`Add(IOrderBook, MarketSchedule)` gains a refusal:

```csharp
        // There is no honest way to write an arbitrary IOrderBook down - the restrictions it was
        // built with are not on its public surface, and might be a combination no Instrument can
        // describe. A venue that silently cannot be rebuilt from its own journal is a worse
        // afternoon than a refusal at the line that caused it, which is the same reason Submit
        // refuses an unregistered symbol where the routing mistake was made.
        if (_journal is not null)
            throw new InvalidOperationException(
                "a journalled group cannot take a pre-built book: it cannot be written down, so " +
                "the journal would not rebuild this venue. Register an Instrument instead, or " +
                "build the group without a journal.");
```

Nothing else changes. `LiveDriver`, `Replay`, `AgentTrace` and every sample keep working untouched,
because both new parameters are optional and default to not journalling - 49 construction sites, no
edits.

## Decisions settled in this step

**A nullable field, not `Journal.None`.** The parent plan says the default is a null-object
implementation; this supersedes it, and that line in `persistence-plan.md` is amended in the same
commit. The reason is the codebase's own habit - dense ladders, indexed loops over `OfType`,
buffers reused rather than allocated - which does not spend a non-inlined interface call per
dispatch on a venue that is not recording. The nullability is contained: two `is not null` checks in
`Sequencer`, two `?.` in `InstrumentGroup`.

**Write-ahead for actions, write-behind for configuration.** Argued in the code comments above, and
the apparent inconsistency is the point: an action's failure mode is a crash whose input you want,
and a registration's failure mode is an exception the caller sees.

**The journal records input, not outcome.** An action the book rejects - an order arriving at a
closed book, a price outside a band - is journalled exactly like one it accepts, because the
rejection is an event and events are derived. There is a test for this, because it is the kind of
thing someone later "fixes".

**No reader yet.** Step 2 defines `IJournalReader` alongside the recovery that gives `Read` a
meaning. `InMemoryJournal.Entries` is enough for this step's tests and is what step 2 builds on.

## Tests

`tests/Circus.Tests/Persistence/JournalTests.cs`, one fixture, following the conventions in
`SequencerTests` - a `Day` constant, `At(hour, minute)`, `Quiet()` for a schedule that stays out of
the way, `TradingDay()` for one that does not, and the `PausingGold` instrument with a 5-tick
volatility band for the interruption case.

The seam:

- **`Dispatch_JournalsClientFlow`.** Two orders submitted, one advance. Two `ActionDispatched`
  entries, carrying those actions, in that order.
- **`Dispatch_DoesNotJournalScheduleTransitions`.** A `TradingDay()` schedule and an advance past
  09:30. The pre-open and open are dispatched - assert that from the returned `Dispatched` list -
  and are absent from the journal, and the client action's journalled `Sequence` is the one it was
  dispatched at, skipping them.
- **`Dispatch_DoesNotJournalInterruptionTicks`.** The paused-at-noon setup: a trade breaches the
  band, the book pauses for two minutes, the resume tick dispatches at 12:02. The tick is not in the
  journal; the trade that caused the pause is.
- **`Journal_IsInDispatchOrderNotSubmitOrder`.** Submit 12:05, then 12:01, then advance to 12:10.
  The journal reads 12:01, 12:05. This is the test that pins the seam's location - it fails if
  anyone moves the append to `Submit`.
- **`Submit_IsNotJournalledUntilDispatched`.** Submit without advancing: no action entry. Advance:
  one. An action lost in the queue at a crash was never durable and never visible, and this is where
  that is written down.
- **`Submit_RefusedAction_IsNotJournalled`.** An unregistered symbol throws at `Submit`; the journal
  is untouched.
- **`Rejections_AreJournalled`.** An order at a closed book. The journal has the action; the
  dispatch has a `CreateOrderRejected`.
- **`Flush_IsOncePerDispatchingAdvance`.** A counting `IJournal` test double: two advances that
  dispatch give two flushes, an advance that dispatches nothing gives none, and every flush follows
  the appends in its batch.

Configuration:

- **`Group_JournalsStartAndInstruments`.** `VenueStarted` first, then one `InstrumentAdded` per
  instrument, each with the instrument and schedule instances handed in.
- **`Group_AddMidSession_RecordsLogicalNow`.** Advance to 12:00, add a second instrument, assert
  `At` is 12:00 and not the group's start.
- **`Group_AddPrebuiltBook_ThrowsWhenJournalling`**, and the converse: the same call on an
  unjournalled group still works, so the refusal is about recording and not about the overload.

The one that matters:

- **`Journal_ReplayedIntoAFreshVenue_ProducesTheSameJournal`.** Record a 2,000-action agent trace
  through a journalled group. Take the journalled actions, build a second journalled group with the
  same instruments and schedules, `Replay.Run` the actions into it, and assert the second journal's
  `ActionDispatched` list equals the first's - actions *and* sequence numbers.

  Equal sequence numbers is the strong half: they only match if the derived dispatches that consume
  the gaps regenerated at the same instants in the same order. That is the whole premise of step 2,
  demonstrated before any recovery code exists, and it is where a mistake in "client flow only"
  surfaces as a failing test rather than as a corrupt venue three steps later.

An assertion helper is worth having rather than repeating - `Actions(journal)` returning the
`ActionDispatched` entries, and `Entries(journal)` for the config ones.

**Equality, and its limit.** `ActionDispatched` compares structurally, because `OrderBookAction` and
its subtypes are records of value members. `InstrumentAdded` does not, really: `MarketSchedule` is a
plain sealed class, and `Instrument.PriceRestrictions` is an `IReadOnlyList<>` compared by
reference. Step 1's tests hand the same instances round, so this is invisible here; step 4 will have
to answer it properly when those entries are serialised and compared across a process. Noted rather
than solved, because solving it now means adding equality to types for a reason that does not exist
yet.

## Benchmark

A third case in `OrderBookThroughputBenchmarks`, beside `ReplayTraceThroughSequencer`:

```csharp
    // The same trace through the same queue, recording as it goes. Against the sequencer case
    // above, the difference is what journalling costs on the dispatch path: one entry allocation
    // and one list append per client action, and a flush per AdvanceTo that does nothing here.
    //
    // The in-memory journal keeps every entry, so allocations grow with ActionCount by design.
    // The number to read is the delta against the sequencer baseline, not the total.
    [Benchmark]
    public int ReplayTraceThroughJournalledSequencer()
```

One decision waits on that number. `Append(JournalEntry)` allocates a small record per dispatched
action; if the delta is large enough to care about, the fix is an `Append(long, OrderBookAction)`
overload for the hot entry, with the record kept for reading. That is a change to make with a
measurement in front of you, not on suspicion - and this benchmark is here so there is one.

## Done when

- `dotnet build` clean, `dotnet test` green, `dotnet run --project samples/Circus.Examples` runs all
  five samples unchanged.
- No existing file's behaviour changes for a caller that passes no journal. The 49 existing
  construction sites are untouched, which is the check that the parameter is genuinely optional.
- `Circus.csproj` has no new package reference.
- The `Sequencer` class comment explains why the append is in the dispatch loop, and
  `persistence-plan.md`'s `Journal.None` line is amended to match what was built.

## Worth watching while building it

- **The `_sequence++` move** is behaviourally identical except when `book.Process` throws, where the
  counter is now advanced for an action that produced nothing. The sequencer's state is already
  incoherent at that point - the action is dequeued and logical now has moved - so this changes
  nothing that was previously recoverable, but it is the one delta in the edit and should be
  understood rather than discovered.
- **`Add(IOrderBook, ...)` throwing** is a new exception path on an existing public method. No
  existing caller can reach it, since it needs a journal and journals are new, but it is worth
  confirming the samples and `AgentTrace` use the `Instrument` overload - they do today.
- **The public surface of a packable assembly grows.** Whether that is a `PackageVersion` bump is a
  release decision and not this step's; it is deferred, not forgotten.
