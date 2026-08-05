# Persistence: journalling the action stream, and recovering from it

A plan, not yet carried out. Nothing below exists in the tree today.

The claim the README already makes is that "a journal of those actions is enough to rebuild a book
by replaying it". That is true of the engine and always has been - `OrderBook` reads no clock and
consults nothing ambient - but nothing writes the journal, so the claim is a property of the design
rather than something the library does. This is the plan to make it something the library does:
journal the sequenced action stream and the instrument group's configuration, recover a venue from
them, and then add snapshots so recovery does not have to replay a whole session to get there.

In-memory first, because the interesting problems are all in *where the seam goes* and *what a
snapshot has to contain*, and neither of them is a storage problem. Durable stores come after, and
are deliberately the last thing.

## What already holds

Three properties the engine has today are the reason this is small rather than a rewrite:

**The book is a pure function of its actions.** Same actions, same events, every time. So a journal
of actions is a complete record - there is no clock reading, no `Guid.NewGuid()`, no ambient
configuration to capture beside it.

**The sequencer's dispatch order is the venue's order of events.** One queue, one dispatch loop, and
ordering by `(Time, Kind, Counter)` where the counter makes every entry distinct. So there is one
order worth recording, and exactly one component that knows it.

**`Replay` already consumes exactly the thing a journal would produce.** A stream of stamped
actions, fed action by action, with the schedules regenerating their own transitions. Recovery is
therefore not new machinery; it is `Replay` pointed at a journal instead of at an
`AgentTrace.Record` result, plus the config needed to build the venue it replays into.

What does *not* hold, and shapes everything below: derived actions are not in the client's stream.
Schedule transitions come from `MarketSchedule.NextAfter`, and interruption ticks come from a book's
own `StatusChanged.ResumesAt`. Neither is submitted by anyone. Both are reproducible - the first
from configuration, the second from the events the replay itself re-emits - which is why the journal
does not record them and why the configuration must be journalled as carefully as the actions.

## Where the journal goes

**In `Sequencer.AdvanceTo`, after the dequeue and before `book.Process`, for `ClientFlow` entries
only.**

```csharp
while (_queue.TryPeek(out _, out var next) && next.Time <= time)
{
    var action = _queue.Dequeue();
    _now = next.Time;

    var (book, schedule) = _books[action.Symbol];

    // Moved up from below, so the number the journal records and the number on the Dispatched
    // record are the same number. No observable change - it is a counter.
    _sequence++;

    // Journalled before the book sees it, and only client flow: a schedule transition is a
    // function of the schedule, an interruption tick a function of an event a replay re-emits,
    // and recording either would mean a recovery that regenerates them dispatches them twice.
    if (_journal is not null && next.Kind == DispatchKind.ClientFlow)
        _journal.Append(new ActionDispatched(_sequence, action));

    var events = book.Process(action);
    dispatched.Add(new Dispatched(_sequence, action, events));

    if (next.Kind == DispatchKind.ScheduleTransition)
        QueueNextTransition(action.Symbol, schedule, next.Time);

    QueueInterruptionTicks(events, next.Time);
}

_now = time;

// Group commit. One flush per AdvanceTo rather than per action, and the caller publishes what
// this returned - so a flush here is a flush before anything caused by these actions is visible
// anywhere. That is the durability rule, and this is the only line that enforces it.
if (_journal is not null && dispatched.Count > 0) _journal.Flush();

return dispatched;
```

The durability rule this buys, stated plainly: **nothing the venue does is visible before the action
that caused it is durable.** No confirm, no fill, no market data message. An action accepted by
`Submit` and still sitting in the queue when the process dies is lost, and that is correct - nothing
observable had happened, so it is indistinguishable from a message lost in the gateway's socket
buffer.

### Where it does not go, and why

**Not `LiveDriver.Submit`.** It is one of three entry points, not the only one. `InstrumentGroup.Submit`
and `Replay.Run` both go straight to `Sequencer.Submit`, so a journal here has holes in it.

**Not `Sequencer.Submit`.** Tempting - it is the single choke point every path converges on, and it
is the acceptance boundary, since the validation that refuses an unstamped, backdated or unrouted
action has just run. It is wrong anyway, because submit order is not dispatch order. `Submit`
accepts anything at or after logical now, so a caller may submit 12:05 and then 12:01, and the
sequencer will dispatch 12:01 first. A journal in submit order therefore holds actions out of time
order, and feeding it back through `Replay.Run` - which advances to each action's instant as it
goes - throws on the first one that moves time backwards. Journalling at dispatch is journalling in
the one order that is guaranteed monotonic, which is what makes the journal streamable back through
the code that already exists.

**Not `OrderBook.Process`.** A book is one instrument. The venue's order is what has to be recorded,
and a book cannot see it.

**Not after publication.** That inverts the durability rule: a subscriber would have seen a print
the venue could not prove it had made.

### Configuration

The action journal is worthless without the venue it replays into. That is `InstrumentGroup.Add`,
and it is the only place the `Instrument` record is visible - `Sequencer.Add` takes an `IOrderBook`,
which publicly carries nothing but a symbol. So the journal is attached to the group:

```csharp
var journal = new InMemoryJournal();
var group = new InstrumentGroup(start, journal);   // appends VenueStarted(start)
group.Add(Gold, tradingDay);                       // appends InstrumentAdded(Gold, tradingDay, at)
```

`InstrumentGroup` hands the journal to the `Sequencer` it constructs, so `group.Sequencer.Submit`
still journals and there is no way to route round it. A bare `new Sequencer(...)` outside a group is
unjournalled by construction, which is fine and worth saying out loud: **the recoverable unit is the
`InstrumentGroup`**, not a lone sequencer.

The other overload - `Add(IOrderBook book, MarketSchedule schedule)`, which takes a pre-built book
with restrictions an `Instrument` cannot yet describe - **throws when a journal is attached**. There
is no honest way to write an arbitrary `IOrderBook` down, and a venue that silently cannot be
rebuilt from its own journal is a worse afternoon than a refusal at the line that caused it. Same
reasoning as `Submit` refusing an unregistered symbol: complain where the mistake was made.

`InstrumentAdded` carries the logical now it happened at, because a group registered mid-session is
a real and different thing from one registered before the day starts - `Sequencer.Add` queues the
next boundary *after* logical now, so where in the stream the `Add` fell changes what the schedule
does. In practice every instrument is added before the venue starts and every config entry sits at
the head of the journal, but recovery is written for the general case because the code allows it.

### Entries

```csharp
public abstract record JournalEntry;

public sealed record VenueStarted(DateTime Start) : JournalEntry;

public sealed record InstrumentAdded(Instrument Instrument, MarketSchedule Schedule, DateTime At)
    : JournalEntry;

public sealed record ActionDispatched(long Sequence, OrderBookAction Action) : JournalEntry;

public sealed record CheckpointTaken(long Sequence, string SnapshotId) : JournalEntry;
```

`Sequence` is the sequencer's own dispatch count, the number already on `Dispatched`. It is the
anchor everything joins on - a snapshot says which dispatch it is consistent as of, and recovery
takes the journal tail strictly after that number. The same join a market data snapshot makes on
the channel's sequence, which `IIncrementalProducer` describes and CME calls `LastMsgSeqNumProcessed`.
Numbers in the journal are not contiguous: derived dispatches consume sequence numbers without
appearing, so a gap is a schedule transition and not a lost record.

```csharp
public interface IJournal
{
    void Append(JournalEntry entry);
    void Flush();
}

public interface IJournalReader
{
    IEnumerable<JournalEntry> Read(long afterSequence = 0);
}
```

The reader lands in step 2 rather than step 1, with the recovery that gives `Read` a meaning: what
it should do with a config entry falling after that sequence is a recovery question, and answering
it without one in front of you means writing the interface twice.

No journal is the default on `InstrumentGroup`, so every existing caller and every test keeps
working untouched. The field is nullable and checked rather than filled with a do-nothing
implementation: this is the dispatch loop, the check is constant-false for an unjournalled venue and
so perfectly predicted, and an interface call that does nothing is not free. See
`persistence-step-1.md`, which settles this and the rest of the step's detail.

### A journal never records its own replay

Recovery feeds journalled actions back through `Sequencer.Submit`, and they dispatch as `ClientFlow`
- so without something to stop it, recovery writes the journal it is reading. The rule lives in one
place, in the journal rather than in the sequencer, as a state the journal is in:

```csharp
journal.BeginRecovery();   // appends are checked against what is being read, not written
...replay...
journal.EndRecovery();     // appends resume, at the end of the log
```

While recovering, `Append` is not merely suppressed: it pops the next expected entry from the reader
and throws if what the replay produced differs. That costs nothing and turns recovery into a
self-checking operation - it catches the whole class of bug where the recovered venue is configured
differently from the recorded one, since a different schedule produces different derived actions,
which changes what client flow meets and eventually what dispatches. Better a loud failure at the
first divergence than a quietly wrong book. `Replay` already says its own equivalence is "asserted
rather than assumed"; this is that habit applied to recovery.

## Recovery

```csharp
public static class Recovery
{
    // Rebuilds a group from the journal, and from the newest snapshot at or before its end if a
    // store is given. The group comes back live: journal appending, ready for a LiveDriver.
    public static InstrumentGroup Restore(IJournalReader reader, IJournal journal,
        ISnapshotStore? snapshots = null);
}
```

One ordered pass over the entries, because config and actions interleave:

1. `VenueStarted` builds the `InstrumentGroup`.
2. `InstrumentAdded` advances the sequencer to `At`, then calls `group.Add`.
3. `ActionDispatched` submits the action and advances to its instant - which is exactly
   `Replay.Run`, one action at a time, so the journal streams rather than being held in memory
   twice.

With a snapshot store, steps 1 and 2 run first to build the venue, the snapshot is applied, and the
pass then skips to entries after the snapshot's sequence.

### A worked example

A venue carrying `GCZ6` and `SIZ6`, both on a 09:00 pre-open / 09:30 open / 17:00 close schedule,
started at 08:00 on 2026-08-04. It has been taking flow all morning. At 14:03:17 the process dies.

The journal, at that moment:

```
      VenueStarted     start=2026-08-04T08:00:00
      InstrumentAdded  GCZ6  tick=0.1  band=50  at=2026-08-04T08:00:00
      InstrumentAdded  SIZ6  tick=0.5            at=2026-08-04T08:00:00
      ActionDispatched seq=3        09:31:02.115  CreateLimitOrder GCZ6 ACME/o-1 buy 5 @ 1000.0
      ActionDispatched seq=4        09:31:02.118  CreateLimitOrder GCZ6 BETA/x-9 sell 5 @ 1000.0
      ...
      CheckpointTaken  seq=812444   snapshot=gcz6-siz6-20260804T130000
      ...
      ActionDispatched seq=901205   14:03:16.902  CancelOrder GCZ6 ACME/o-44182
```

Dispatches 1 and 2 are the schedule's pre-open and open. They are not in the journal, and they are
not missing: they are what `MarketSchedule` says, and a recovery that builds the same schedule
regenerates them at the same instants with the same numbers.

Recovery, warm:

1. **Build the venue.** `VenueStarted` gives `new InstrumentGroup(2026-08-04T08:00)`. The two
   `InstrumentAdded` entries register `GCZ6` and `SIZ6` with their schedules, each queueing its
   first boundary - pre-open at 09:00 - exactly as the original run did. The journal is in recovery
   mode throughout, so none of this is written twice.
2. **Apply the snapshot** for sequence 812,444 (13:00:00). Books get their status, resting and
   triggered orders, stop book, client-order-id index, sequence and trade counters, indicative
   quote, limit state and restriction anchors. The sequencer gets `LogicalNow = 13:00:00.000` and
   `Sequence = 812444`. The channel gets its published sequence.
3. **Re-queue the derived work**, which the snapshot deliberately does not contain. For each book,
   `QueueNextTransition(symbol, schedule, 13:00:00)` gives the 17:00 close - the same single pending
   transition the original queue held. Each book's restored `_resumeAt`, if it has one, becomes an
   interruption tick at that deadline. The submission counter restarts at zero, which changes
   nothing: ties between a transition and a tick are settled by kind before counter, and ties within
   a kind fall to registration order in both worlds.
4. **Replay the tail.** Entries after sequence 812,444 stream through `Replay.Run`: dispatches
   812,445 through 901,205. Roughly 89,000 actions rather than 901,000 - and each one is verified
   against the journal as it goes, per the recovery mode above.
5. **Go live.** `journal.EndRecovery()`, then hand `group.Sequencer` to a `LiveDriver` with a real
   clock. The first live submit is stamped by that clock, and a clock reading behind 14:03:16.902 is
   refused by `Sequencer.Submit` rather than quietly reordering the venue - which is the right noise
   for a machine that came back with a bad clock.

Recovery, cold - no snapshot store, or no checkpoint yet taken - is the same pass with step 2 and 3
skipped and step 4 starting from the first `ActionDispatched`. Same final state, slower. That is the
property worth protecting: **a checkpoint is an optimisation, never a semantic.** Any checkpoint plus
its tail equals the whole journal, and a test asserts exactly that.

### What recovery does not restore

Worth being explicit, because each of these is somebody assuming otherwise:

- **Participants.** An agent's `OrderTracker` and `MarketView` are its own state, rebuilt from the
  events it sees. A recovered venue does not hand them back; an agent reconnecting starts from a
  snapshot of the feed like any other subscriber.
- **Subscriber positions.** Recovery replays dispatches, so the channel republishes the same
  messages with the same sequence numbers - identical by construction, since publication is a pure
  function of the events. Publication is suppressed during recovery by default; it can be enabled
  deliberately, and that is what makes it usable as a gap-fill.
- **Gateway sessions.** Connections, sequence numbers on the wire, and who was logged in are the
  gateway's problem and are not modelled here at all.

## Snapshots

A checkpoint is taken by the host between `AdvanceTo` calls - the natural quiescent boundary, where
no action is half-processed - and recorded in the journal as `CheckpointTaken(sequence, id)` so
recovery knows where it fell.

```csharp
var dispatched = driver.Tick();
publish(dispatched);
if (clock.GetCurrentTime() - lastCheckpoint > every) venue.Checkpoint();
```

The journal entry is what makes this deterministic rather than merely convenient: a replay from zero
can be asked to capture at the same sequence and compare, which is how a snapshot gets tested at all.

**Why this is not an action, given that `IIncrementalProducer` argues snapshots should be.** That
argument is about *market data* snapshots, and it is right: everything a consumer knows must arrive
as an event, so a subscriber's recovery image has to be produced by dispatching an action and
answering with an event, or it cannot be reproduced from the journal. An operational checkpoint is
not consumer-visible. Routing an image of hidden iceberg reserves, stop orders and company ids
through `MarketDataChannel` to reach a disk writer would put every participant's private state on
the event stream to save a function call. The two snapshots are different things with different
audiences, and the market data snapshot feed - still unbuilt - stays action-driven when it lands.

### What a book snapshot has to contain

This is the part that is actually difficult, and the reason snapshots come after recovery rather
than with it.

```csharp
public sealed record VenueSnapshot(long Sequence, DateTime LogicalNow, long ChannelSequence,
    IReadOnlyList<BookSnapshot> Books);
```

Per book, from `OrderBook`'s fields: `Status`, `LastActionTime`, `NextSequenceNumber`,
`NextTradeId`, `LastTradedPrice`, `ResumeAt`, `ResumeTo`, `LimitState`, `IndicativeQuote`. Then:

**Every order, not every live order.** `_clientOrderIndex` holds every `(CompanyId, ClientOrderId)`
pair ever assigned, permanently reserved, and it is what enforces per-client uniqueness and
ownership. A snapshot that carries only resting orders comes back accepting a client order id that
was used this morning, which is a silent correctness failure rather than a visible one.

**Queue position, and how it is restored.** Priority within a level is the intrusive list's order.
It does not need capturing: `PriceLadder.Add` appends at the tail, and everything that costs an
order its priority - a reprice, an iceberg replenish, a quantity increase - bumps `SequenceNumber`
and re-adds. So within a level, list order is ascending `SequenceNumber`, and restoring means adding
each level's orders in that order. This is an invariant the restore depends on, so it gets a test of
its own rather than a comment.

**Where each order rests**, which is derivable - working ladder, stops ladder, or nowhere - from
status and type, the way `Matcher.RestsInStops` already decides it. And `RestingTick`, which is
stored rather than re-derived precisely because price and ladder disagree for the length of an
update.

**Restriction state.** Each `IPriceRestriction` grows `Capture`/`Restore`: the rolling
`_recentTrades` queue and `_sessionPriceTicks` on `VolatilityBandRestriction`, three anchors on
`OrderPriceBandRestriction`, `SessionLimitAnchor`'s reference and width for the daily limit and the
circuit breaker. A restriction restored without its window is a band that has forgotten the last ten
minutes of trading, which fails open - the worst direction for a safety feature to fail in.

**The instrument instance.** `InternalOrder` holds an `Instrument` reference, and restrictions on
that record compare by reference. Restore reattaches the book's own instance rather than
deserialising a second one per order.

**The feed.** `InstrumentStatusDataProducer` accumulates a composite no single event carries, and
`MarketByPriceIncrementalProducer` tracks the window it last published. Both are needed, or the
first message after recovery is a delta against a book the producer never published.

### What deliberately is not in it

- **The sequencer's queue.** One pending transition per book, re-derivable from the schedule and
  logical now; interruption ticks, re-derivable from each book's `_resumeAt`. So no `PriorityQueue`
  is ever serialised. The equality that makes this safe -
  `NextAfter(logicalNow) == NextAfter(lastDispatchedTransitionTime)`, because anything between them
  would itself have been dispatched - is a proof obligation, and gets a test.
- **The submission counter**, for the reason given in the worked example.
- **The trading phases**, rebuilt from `Instrument.MatchingAlgorithm`. This assumes the phase
  algorithms hold nothing across actions - an auction's struck price and a pro-rata level's pending
  allocations are described as run-scoped, and the assumption needs *verifying* rather than
  believing. If either survives a `Match` call, it joins the capture and the comment in
  `BuildPhases` needs correcting.

## Storage

`Circus` has no dependencies and the README says so, so the split is:

- **`Circus`** carries `IJournal`, `IJournalReader`, `ISnapshotStore`, the entry and snapshot
  records, and in-memory implementations. No package references, nothing to serialise.
- **`src/Circus.Persistence`**, a new project, carries the durable stores and may take package
  references. Not packable to begin with, like `Circus.Agents`.

In order of when they are worth building:

**In-memory.** A `List<JournalEntry>` and a dictionary of snapshots. Not a placeholder - it is what
every test and every deterministic sample uses, and it is what makes "record a run, recover it,
assert the two are identical" a unit test rather than a fixture with a temp directory.

**Append-only file.** Length-prefixed frames, a type tag per entry, a CRC per frame, and a torn
final frame treated as absent rather than as corruption - a process killed mid-write is the normal
case, not an exception. `System.Text.Json` with `[JsonDerivedType]` for the polymorphism, because a
journal you can read with `less` is worth a great deal while the format is still moving. Two things
to pin with tests rather than assume: `DateTime.Kind` round-trips (a restore that shifts every stamp
by an offset is a very quiet bug), and `decimal` prices round-trip exactly.

**SQLite.** One row per entry keyed by sequence, which buys indexed reads of a tail, and one
transaction spanning a snapshot write and its `CheckpointTaken` entry - so the two cannot disagree
about whether a checkpoint happened. That transactional pairing is the actual reason to reach for
it, ahead of any query convenience.

**Parquet is an archive format, not a journal format.** It is columnar and wants large row groups,
which is the opposite of what a write-ahead log needs, and a half-written row group after a crash is
not recoverable the way a torn frame is. The right shape is a post-session export - the day's
actions and events written once, for analysis and for feeding backtests - sitting beside the journal
rather than replacing it. Worth building, but as a reader of the journal, not as the journal.

Throughput: journalling costs one append per client action on the dispatch path and one flush per
`AdvanceTo`, which is the group-commit boundary falling out of the shape that already exists. The
benchmark grows a journalled variant so the cost is a number rather than an opinion.

## Sequence

Each step compiles and leaves the suite green.

1. **The seam and the in-memory journal.** `IJournal`, the entry records, `InMemoryJournal`; wired
   into `Sequencer.AdvanceTo` and `InstrumentGroup`. Nothing reads a journal yet. Tests pin that
   client flow is recorded in dispatch order and derived actions are not. Planned in detail in
   `persistence-step-1.md`.
2. **Cold recovery.** `IJournalReader`, `Recovery.Restore` over config plus actions, no snapshots,
   with the verifying recovery mode. This is the step that proves the README's claim.
3. **Capture and restore.** `VenueSnapshot`, `BookSnapshot`, per-restriction capture, `ISnapshotStore`,
   `InMemorySnapshotStore`, `CheckpointTaken`, `venue.Checkpoint()`. Warm recovery.
4. **`Circus.Persistence`.** File journal first, SQLite second, Parquet export third.
5. **A sample and the README.** `RecoveryExample`: run a seeded agent swarm at a journalled venue,
   drop it mid-session, recover, and show the recovered venue producing the same market data as the
   one that never died. Then the README's persistence claim stops being a claim.

## Tests worth having

- **A recovered venue is indistinguishable from one that never died.** Run a seeded swarm at a
  journalled venue, recover from the journal into a fresh group, and drive both with the same
  remaining flow: identical channel messages, identical book snapshots. The whole plan in one test.
- **Warm equals cold.** Recovery from a checkpoint plus tail equals recovery from the whole journal.
  This is what keeps checkpoints an optimisation.
- **Recovery is verified, not hoped for.** Recover into a group configured with a different schedule
  and assert it throws at the first divergence rather than producing a plausible book.
- **Derived actions regenerate exactly.** Dispatch sequence numbers across a recovery match the
  original run's, including the ones the journal does not contain.
- **A snapshot round-trips priority.** Restore a level holding six orders, one of them a replenished
  iceberg that requeued, and assert the next sweep fills them in the original order.
- **Client order ids stay reserved across a snapshot.** Reuse an id assigned before the checkpoint
  and assert the rejection.
- **Restrictions keep their memory.** A volatility band whose rolling window straddles a checkpoint
  breaches on the same trade it would have breached on without one.
- **A torn final frame is absent, not fatal.** Truncate a file journal mid-entry and recover.
- **`DateTime.Kind` and `decimal` survive the round trip.**

## Decisions taken, worth challenging

- **Journal at dispatch, not at acceptance.** Costs the ability to tell a client "accepted and
  durable" before the next tick, and loses queued-but-undispatched actions on a crash. Buys a
  journal in the only monotonic order there is, replayable by code that already exists. A venue
  wanting the stronger acceptance guarantee should journal at submit *as well*, into a different
  log, and reconcile - not move this one.
- **Client flow only.** Derived actions are regenerated rather than recorded, which makes the
  journal smaller and makes configuration part of the recoverable state whether you like it or not.
  The alternative - journal every dispatch and replay them straight into books, bypassing the
  sequencer - removes the dependency on configuration being right and costs the ability to hand the
  recovered venue back to a live sequencer without reconstructing its queue anyway.
- **The recoverable unit is the `InstrumentGroup`.** A lone `Sequencer` cannot be journalled because
  it never sees an `Instrument`. That is a real limitation and it is the right one: the group is
  what has a channel, a start time and a set of instruments, which is what a venue is.
- **`Add(IOrderBook, ...)` throws when journalling.** Refuses the one construction that cannot be
  written down, at the line that does it.
- **Checkpoints are host-driven, not action-driven.** Argued above against
  `IIncrementalProducer`'s reasoning, which applies to market data snapshots and not to this one.
- **Snapshots hold no queue and no counter.** Both are derivable, and deriving them is what keeps a
  snapshot a statement about books rather than a serialisation of the engine's internals.
