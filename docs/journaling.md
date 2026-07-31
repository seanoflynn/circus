# Journaling and snapshotting

A plan for making a running venue survive the process that hosts it. Nothing here is built yet;
this is the shape it should take and the reasoning behind it, written down before any of it is
committed to code.

## What the problem actually is

An `OrderBook` is working state and says so in its own header comment: it holds a day's orders for
as long as the process does and writes nothing down, because durability belongs to whatever
journals the action stream on the way in and rebuilds a book by replaying it. That is the design
this follows through on. The journal is the venue's record of truth; the books, the ladders and the
market data producers are a cache of the fold over it.

Two things need to survive a restart, and they are not the same thing:

- **State** — what the books hold, so an order resting before the crash is still resting after it.
- **Position** — where the venue got to in its own output, so a subscriber counting channel
  sequence numbers does not see them restart at 1, skip a block, or repeat one.

Position is the harder half and the one a naive "just replay the actions" answer gets wrong.

## What has to be recorded, and what does not

The sequencer takes three sources: client flow through `Submit`, schedule transitions derived from
each book's `MarketSchedule`, and interruption ticks derived from the books' own `StatusChanged`
events. Only the first is external. The other two are functions of the registration and of book
state, so recording them buys nothing for rebuilding — they regenerate on their own during a
replay, which is exactly what `Replay.Run` already relies on.

Nondeterminism enters the venue in one place: `LiveDriver.Submit`, which stamps an arriving action
with the clock. After that stamp, everything downstream is a pure function of the actions
(`DeterminismTests` pins this down, and it is the property the whole plan rests on). So the minimum
recoverable record is:

1. **The venue's configuration** — start time, and for each instrument its `Instrument`,
   `MarketSchedule` and feed depth. A journal that does not say what venue it belongs to cannot be
   replayed into one.
2. **Every accepted client action, with the stamp it was accepted under.**

That is enough to rebuild state. It is not enough to resume position, so a third stream is
recorded as well:

3. **Every published `ChannelMessage`**, with its channel sequence.

And a fourth, optional and much the largest, recorded because it is what a drop copy, a regulatory
record and an offline analysis all want, and because it is what lets recovery check itself:

4. **Every `Dispatched`** — the venue sequence, the action, and the events it produced.

Stream 4 is switchable. Streams 1–3 are not.

## The records

One stream of records, in the venue's dispatch order, in the same idiom `OrderBookAction` and
`OrderBookEvent` already use — an abstract record with sealed subtypes, so a row store can hold one
table with a discriminator and a columnar store can fan the subtypes out into one file each.

```csharp
namespace Circus.Journaling;

// One entry in the venue's record. Time is the instant the venue was at when this was written -
// logical, never wall clock, so a journal read back tells the same story it told live.
public abstract record JournalRecord
{
    public required DateTime Time { get; init; }
}

// Written once, first. Everything after it is meaningless without it: a journal replays into the
// venue it was written by or into nothing at all.
public sealed record VenueConfigured(VenueConfig Config) : JournalRecord;

// Client flow, as accepted. Written after the sequencer has taken it, so the journal holds no
// action the venue refused - a rejected symbol or a backwards stamp is a gateway's problem and
// not part of the venue's history.
public sealed record ActionSubmitted(OrderBookAction Action) : JournalRecord;

// One dispatch and what came back. The audit stream, and what recovery checks itself against.
public sealed record ActionDispatched(long Sequence, OrderBookAction Action,
    IReadOnlyList<OrderBookEvent> Events) : JournalRecord;

// One message as it left a channel. The channel sequence here is the venue's position: recovery
// resumes at the first sequence past the last of these.
public sealed record MessagePublished(long ChannelSequence, MarketDataEvent Data) : JournalRecord;

// A snapshot was taken at this position. The payload lives in the snapshot store, not here.
public sealed record SnapshotTaken(long Sequence, string SnapshotId) : JournalRecord;
```

`VenueConfig` is a plain description — start time and a list of `(Instrument, MarketSchedule,
MaxLevels)` — and is what `InstrumentGroup.Add` already receives. Recording it means a journal can
reconstitute its own venue rather than needing one hand-built to match.

## The interfaces

Deliberately small, because they have to be implementable over a `List<T>`, a SQLite table and a
Parquet dataset without any of the three distorting the other two.

```csharp
public interface IJournal
{
    // Appends in venue order. Ordering is the caller's, not the store's: the sequencer already
    // decided it.
    void Append(JournalRecord record);

    // Makes everything appended so far durable. A store with no durability to offer implements
    // this as a no-op, which is the honest answer for the in-memory one.
    void Flush();
}

public interface IJournalReader
{
    // Every record from `fromSequence` on, in the order they were appended. Streamed rather than
    // materialised: a day's journal is not something to hold twice.
    IEnumerable<JournalRecord> ReadFrom(long fromSequence = 0);
}

public interface ISnapshotStore
{
    void Save(VenueSnapshot snapshot);

    // The newest snapshot at or before `sequence`, or null. Recovery asks for the newest one it
    // can trust and replays the journal after it.
    VenueSnapshot? Latest(long atOrBeforeSequence = long.MaxValue);

    void DeleteBefore(long sequence);
}
```

`InMemoryJournal` implements both journal interfaces over a `List<JournalRecord>` with a `Flush`
that does nothing; `InMemorySnapshotStore` holds a `SortedList<long, VenueSnapshot>`. Neither
survives the process, which sounds like it defeats the point and does not: they are how the
protocol, the recovery path and the equivalence tests get built and proven before a byte of
serialization exists. A crash test against an in-memory journal is a perfectly good crash test —
it drops the engine, keeps the journal, and rebuilds.

**Where these live.** The interfaces, the records and the in-memory implementations go in
`src/Circus/Journaling/`, because Circus ships with no dependencies and these need none. SQLite and
Parquet arrive as `Circus.Journaling.Sqlite` and `Circus.Journaling.Parquet` — separate packages,
separate dependencies, no change to the core.

## The central object

`InstrumentGroup` is already the thing that owns both a `Sequencer` and a `MarketDataChannel`, and
those are precisely the two components whose positions have to stay in step. It becomes the
coordinator rather than a new type being introduced beside it.

The change that matters is not the constructor argument — it is closing the seam. Today a caller
does this:

```csharp
var dispatched = group.Sequencer.AdvanceTo(time);
var messages = group.Channel.Publish(dispatched.SelectMany(d => d.Events).ToList());
```

Two steps a caller can do in the wrong order, do one of and not the other, or interleave a snapshot
in the middle of. The group takes the loop over instead:

```csharp
public sealed class InstrumentGroup
{
    public InstrumentGroup(DateTime start, IJournal? journal = null, ISnapshotStore? snapshots = null);

    // Validate, then journal, then queue. Nothing is written for an action the sequencer refuses.
    public void Submit(OrderBookAction action);

    // Advance, publish and record, in the one order that is safe. Returns the messages to hand
    // out - already durable by the time the caller has them.
    public IReadOnlyList<ChannelMessage> AdvanceTo(DateTime time);

    // Between dispatches only, which is why it lives here: nothing else is in a position to know
    // that the loop is not currently half way through one.
    public VenueSnapshot Snapshot();

    public static InstrumentGroup Recover(IJournalReader journal, ISnapshotStore? snapshots = null);
}
```

`Sequencer` and `Channel` stay exposed, because tests and `Replay` use them directly and a
sequencer with no journal is still a legitimate thing to run. But the journaled path is one call,
and a journaled group is the thing a live venue is built on.

`LiveDriver` keeps its job — deciding what time it is — and calls `group.AdvanceTo` rather than
`sequencer.AdvanceTo`.

## Normal trading

Per action, in order:

1. `LiveDriver.Submit` stamps the action with the clock.
2. `group.Submit` hands it to the sequencer. If the sequencer refuses it — unknown symbol, a stamp
   behind logical now — it throws and nothing is written.
3. Accepted, the action is appended as `ActionSubmitted`. Not flushed yet.
4. On the next `AdvanceTo`, the sequencer dispatches everything due. For each dispatch: append
   `ActionDispatched` (if the audit stream is on), publish the events through the channel, append a
   `MessagePublished` per resulting message.
5. `Flush` once, at the end of the advance, before the messages are returned to the caller.
6. The caller sends them.

The single rule those steps exist to enforce is **durable before visible**. A message a subscriber
has seen must already be in the journal, because recovery reproduces the journal and anything a
subscriber saw that the journal does not hold is a fill that un-happens. Everything else about the
ordering is negotiable; that is not.

Flushing once per advance rather than once per action is what makes the rule affordable. A tick
that dispatches four hundred actions pays for one flush, and the actions submitted between ticks
are unflushed only while they are also invisible — nothing has been dispatched from them, so
nothing has been published, so losing them costs a client an ack it never received. A gateway that
retries is made safe by the book itself: `_clientOrderIndex` reserves every `(CompanyId,
ClientOrderId)` pair permanently, so a resent create is rejected with `OrderIdAlreadyUsed` rather
than duplicated.

Cost, for the in-memory backend: one record per action, one per dispatch, one per message, all of
them references to objects that already exist. No copying and no serialization. The audit stream is
the expensive one by volume and is the one that can be switched off. The throughput benchmark
should gain a journaled variant so the overhead is a number rather than a claim.

## Snapshots

A journal alone recovers a venue by replaying the whole day. That works and is the correct
fallback, but the time it takes grows all day, so a snapshot bounds it.

```csharp
public sealed record VenueSnapshot(
    string Id,
    long Sequence,            // the venue dispatch count this was taken after
    DateTime LogicalNow,
    long ChannelSequence,
    long SequencerCounter,
    IReadOnlyList<PendingEntry> Pending,   // the sequencer's queue: one transition per book, plus ticks
    IReadOnlyList<OrderBookState> Books,
    IReadOnlyList<FeedState> Feeds);
```

Two things make the payload much smaller than it first looks.

**Ladders do not need serializing.** A `PriceLadder` is an index, and time priority within a level
is a function of `InternalOrder.SequenceNumber` — the type says so itself: the exchange order id is
derived from the sequence number so it cannot drift. Restore adds the orders back in sequence-number
order and the ladders rebuild themselves with priority intact. `OrderBookState` is a flat list of
orders plus a handful of scalars: status, last action time, next sequence number, last traded price,
the resume deadline and where it returns to, the limit state, the indicative quote. The client order
index is derived from the orders on the way back in.

**Most market data producers are stateless.** `TradeDataProducer`, `FullBookDataProducer` and
`IndicativePriceDataProducer` compute their output from the events they are handed and hold nothing
between calls. Only `LevelDataProducer` (its published levels) and `SecurityStatusDataProducer`
(status, reason, resume time, limit state) carry state, and both are small. `FeedState` is those
two.

Price restrictions carry a reference anchor each and, for `VolatilityBandRestriction`, a rolling
window of recent trades — a few tens of entries. `IPriceRestriction` gains capture/restore
alongside its existing `OnTrade`/`OnSessionChange` hooks.

The pending queue is captured explicitly rather than re-derived. Re-deriving it is very nearly
sound — `MarketSchedule.NextAfter(LogicalNow)` gives back the same transition, and a book's own
resume deadline gives back its tick — but the submission counters that break ties between two books
queued at the same instant would be reassigned, and the venue's dispatch order is not something to
reconstruct approximately. It is one entry per book plus a handful of ticks. Capture it.

**What it does not hold:** completed orders. They exist to keep the client-order index honest and
they grow all day. A snapshot holds live orders plus the set of retired `(CompanyId,
ClientOrderId)` pairs — the keys, not the orders. If a snapshot needs to reconstruct a completed
order's detail, the audit stream has it.

**Cadence.** On demand, on a `CloseTrading` that ends the trading day (the natural boundary, and
the point where the day's orders have just been retired), and optionally every N dispatches. Always
between dispatches, never inside one, which is why `Snapshot()` is on the group.

**Retention.** Once a snapshot at sequence S is stored and durable, journal records before S are
only needed for audit, not for recovery. `DeleteBefore(S)` is the hook; whether a venue actually
calls it is a policy decision, and for a regulated one the answer is no.

## Recovery

```csharp
var group = InstrumentGroup.Recover(journal, snapshots);
var driver = new LiveDriver(group.Sequencer, clock);
```

What that does:

1. **Read the config.** `VenueConfigured` is the first record. Build the sequencer, the books, the
   schedules and the feeds from it — the same registration path `Add` uses, so there is one way to
   build a venue and recovery is not a second one that can drift.
2. **Restore the newest usable snapshot**, if there is one. Books, feeds, sequencer counters,
   pending queue, logical now, channel sequence. If there is none, start from the config and
   sequence 0.
3. **Replay the journal from the snapshot's position.** Every `ActionSubmitted` goes to
   `sequencer.Submit`, and logical time advances to each action's own instant exactly as
   `Replay.Run` does it — so schedule transitions and interruption ticks regenerate in their proper
   places rather than being read back.
4. **Consume the published stream.** Every message the replay produces is matched against the next
   `MessagePublished` record. Same sequence, same content: discard it, the subscriber already has
   it. Different content: stop, loudly. A divergence here means the fold is not deterministic, and
   a venue that quietly carries on telling clients something other than what it told them before
   the crash is worse than one that refuses to start.
5. **Catch up to the watermark.** When the last `ActionSubmitted` has been replayed there may still
   be `MessagePublished` records left over — a close or an interruption tick that fired after the
   last client action. Advance logical now to the time on the last journaled record, which
   dispatches them, and keep consuming. Recovery is complete when the journal is exhausted and the
   replay has produced exactly the messages it holds.
6. **Resume.** The channel is at the sequence it left off at, the books hold what they held, and
   the first live tick advances from the watermark to whatever the clock now reads.

Step 4 is the part that makes position work, and it is worth being explicit about why it is
suppression rather than a stored cursor. The re-derived stream is deterministic, so message N after
recovery is byte-for-byte the message N that went out before the crash. Suppressing up to the last
journaled sequence therefore cannot skip or duplicate anything, whatever cadence the crash
interrupted — and comparing while suppressing turns the assumption into an assertion on every
single message.

## When it goes wrong

| What happened | What recovery does |
| --- | --- |
| Crash after an action was journaled, before it was dispatched | Replay re-submits it and dispatches it. Nothing had been published from it, so no subscriber sees anything odd. |
| Crash after dispatch, before publish | Replay re-derives the same events, publishes them, finds no journaled messages to suppress, and sends them. The dispatch had never become visible. |
| Crash part way through publishing one dispatch's messages | The per-message records say exactly how far it got. Suppression eats the sent ones, the rest go out. |
| Crash between accepting an action and flushing it | The action is lost. It was never dispatched and never published, so nothing downstream depended on it; the client got no ack and retries, and the client-order index makes the retry safe. |
| Torn record at the tail of a durable journal | Truncate to the last complete record. Durable-before-visible guarantees nothing past it was ever visible. A backend must be able to detect a partial record — length-prefix-and-checksum for a file, a transaction for SQLite. |
| The journal store fails mid-session | Fail-stop. `Append` or `Flush` throwing means the group stops dispatching and the venue halts. A venue that trades while unable to record what it traded has no way back. |
| A schedule transition was due during the outage | It is still in the pending queue after recovery, and the first live tick past its time dispatches it, stamped at its scheduled instant rather than at the restart. Deterministic, and identical to what a replay would do. It does mean an 09:00 open can be published at 09:12; a venue that would rather not should not be restarted into a session it missed the start of. |
| The clock reads behind the recovered watermark (an NTP correction across the restart) | `Sequencer.AdvanceTo` throws, as it already does. Loud is correct here — the alternative is silently reordering a venue. |
| A gateway resends orders it did not see acked | Duplicate creates are rejected by the client-order index. Cancels and updates naming an order that is already gone are rejected the same way they would be in normal trading. |
| The re-derived stream disagrees with the journal | Recovery refuses to complete and names the sequence. This is the determinism regression test firing in production, and the only safe response is not to start. |
| A book was registered mid-session before the crash | The config stream holds the registration in the order it happened, and the sequencer's existing caveat applies unchanged: a book registered mid-session is not caught up, and whoever registered it decided what that meant. Recovery reproduces that decision rather than making a new one. |

## Order of work

1. **Records and the in-memory journal.** `JournalRecord` and subtypes, `IJournal`,
   `IJournalReader`, `InMemoryJournal`. No wiring. Tests: append and read back, ordering preserved.
2. **The group owns the loop.** `InstrumentGroup.AdvanceTo` publishing and journaling in one call,
   `Submit` journaling accepted actions, `VenueConfigured` written on construction and `Add`.
   `LiveDriver` and `Replay` moved onto it. Tests: a journaled group produces the same messages as
   the manual wiring did, and the journal holds what it should.
3. **Recovery without snapshots.** `InstrumentGroup.Recover`, replay-with-suppression, the
   watermark catch-up, the divergence check. This is the phase that proves the design. Tests: run
   `OrderFlowSimulator` traces across several instruments and several seeds, cut at a random
   dispatch, rebuild from the journal, and assert the recovered venue's subsequent messages and
   final book state are identical to those of a venue that never crashed. Cut mid-publish too.
4. **Snapshots.** `OrderBookState`, `FeedState`, `IPriceRestriction` capture/restore,
   `VenueSnapshot`, `InMemorySnapshotStore`, `Snapshot()` and its use in `Recover`. Tests: the same
   equivalence property as phase 3, from snapshot-plus-tail rather than from the whole journal, and
   a direct test that a snapshot taken and restored immediately is a no-op.
5. **Durability semantics.** Fail-stop on append failure, torn-tail truncation, flush accounting.
   Meaningless in memory, so it lands with the first durable backend rather than before it.
6. **SQLite.** `Circus.Journaling.Sqlite` — one table per record type, a transaction per flush.
   Recovery and snapshot tests reused wholesale against it; that they are reusable is the point of
   the interfaces being this small.
7. **Parquet.** `Circus.Journaling.Parquet` — write-optimised for analysis rather than recovery, so
   likely a sink for the audit and market data streams and not the input log. Worth deciding at the
   time whether it is a journal backend at all or an exporter that reads one.

Phases 1–4 are the plan proper. 5–7 are what the interfaces are shaped to allow.
