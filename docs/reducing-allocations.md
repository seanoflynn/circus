# Reducing allocations

A matching engine is judged on the tail, not the mean, and in a managed runtime the tail is
mostly the collector. Every object a hot path allocates is gen0 pressure, and gen0 pressure is
what turns a 2 µs action into a 200 µs one somewhere unpredictable. Throughput barely notices;
the 99.9th percentile notices immediately.

Nothing here is about making the engine cleverer. It is about the same work touching fewer
objects, and it is mostly a question of who owns the containers - the `IReadOnlyList` a book
hands back, the `IList` a producer returns, the scratch lists a sweep builds and drops.

`PriceLadder` already did this once for the book's own storage, replacing a
`SortedDictionary<long, SortedDictionary<long, InternalOrder>>` and its node-per-order with an
array indexed by tick and an intrusive list threaded through `InternalOrder`. The plan below is
that same move applied to everything downstream of it.

## What is measured, and what is not

The counts in the next section were arrived at by reading the code, not by running anything. They
are good enough to rank the work and not good enough to report as results. Stage 0 exists to
replace them.

## Where the allocations are

Counted per action, for a book in continuous trading under price-time.

### A create that rests without trading - roughly 11 objects

| Site | What | Count |
| --- | --- | --- |
| `OrderBook.ResumeIfDue` | `new List<OrderBookEvent>()` on every action, due or not | 1 |
| `OrderBook.Handle` | every arm returns a freshly built `List<OrderBookEvent>`, `AdvanceTime` included | 1 + backing array |
| `OrderBook.Process` | `events.AddRange(...)` grows the outer list | 1 |
| `CreateOrder` | `new InternalOrder(...)` | 1 |
| `InternalOrder.ToOrder` | the `Order` snapshot - 21 members, ~200 bytes | 1 |
| `InternalOrder.ExchangeOrderId` | `SequenceNumber.ToString()`, recomputed on every read | 1 |
| `CreateOrder` | `CreateOrderConfirmed` | 1 |
| `OrderBook.Match` | `pendingImmediateOrCancelStops` list, allocated whether or not a stop is IOC | 1 |
| `OrderBook.Match` | `priceTicks => CheckTradeRestrictionBreach(priceTicks, time)` - display class and delegate | 2 |
| `Matcher.Run` | the `yield return` iterator's state machine | 1 |

Only three of those are the order. The rest is bookkeeping around it.

### Each trade within a sweep - roughly 12 more

Two `FillOrderConfirmed` events, each with its own `Order` snapshot and its own `ExchangeOrderId`
string, is six. The `TradeExecuted` outcome record is one. `_nextTradeId.ToString()` is one.
`GatherTriggeredStops` calls `EnumerateFromBest()` on both stop ladders after every single trade -
two iterator state machines - and when anything is triggered it builds a `SortedDictionary` and
`ToList`s it.

The stop scan is the cheapest thing on this page to fix: `TryGetBest` already answers "is the
nearest stop within reach" in two array reads, and almost always the answer is no.

### Pre-open, per action - the worst of them

`TakeIndicativeQuoteChange` runs on every action, and in pre-open it lands in
`AuctionMatchingAlgorithm.TryQuoteIndicative`, which materialises both ladders into lists via
LINQ, concatenates and de-duplicates the candidate ticks, and then for each candidate runs two
`Where(...).Sum(...)` passes over those lists. That is O(levels²) in time and roughly `8 ×
candidates` allocations, repeated for every action the pre-open session sees, to answer a question
whose answer usually has not changed.

`ProRataMatchingAlgorithm.ComputeAllocations` has the same shape at a smaller scale, per level
entered: a `List<InternalOrder>`, an array, `Enumerable.Range(...).OrderByDescending(...)
.ThenBy(...).ToList()`, and a closing `Where(...).OrderBy(...).Select(...).ToList()`.

### Market data - roughly 35 per dispatch at the default depth

This is where most of the garbage is, and it is generated whether or not anything changed.

`LevelDataProducer.Process` rebuilds and emits a full `LevelsDataEvent` on every dispatch: two
`Snapshot` calls, each a `Take().Select().ToList()` over a `SortedDictionary<decimal, LevelState>`,
producing a `Level` record per level - up to 20 of them at `maxLevels: 10` - wrapped in a
single-element array. Most actions do not move the top ten at all.

Around it: `MarketDataChannel.GroupBySymbol` allocates a single-element array and then a boxed
array enumerator on the common single-symbol path; `InstrumentFeed.Collect` copies each producer's
`IList<T>` into a combined list; each producer allocates its own list to be copied out of.

### Sequencing - 3 to 4 per action

`AdvanceTo` allocates a `List<Dispatched>` per call, and `Replay` calls it once per action.
`QueueInterruptionTicks` runs `events.OfType<StatusChanged>()` - an iterator per dispatch - to find
something that is present in a small fraction of dispatches. On the live path,
`TimestampingOrderBook` and `LiveDriver` each clone the action record to stamp it.

## The decision everything hangs off

`IOrderBook.Process` returns `IReadOnlyList<OrderBookEvent>`, and today that list is fresh every
call. Whether it stays fresh is the one choice that determines how much of the rest is even
available, so it comes first.

**Option A - keep allocating.** Cap the work at reusing internal scratch (the `ResumeIfDue` list,
the pending-stops list) and leave the returned list alone. Safe, no contract change, and leaves
two to three objects per action plus the growth arrays on the table.

**Option B - reuse the list, return it as `IReadOnlyList`.** One buffer per book, cleared per
action. Cheapest to implement and the worst failure mode: a consumer that keeps the reference sees
it silently refill on the next action. Nothing in the type system says otherwise and nothing fails
at compile time.

**Option C - reuse the buffer, return an `EventBatch`.** A readonly struct over the book's buffer
with `Count`, an indexer, a struct `GetEnumerator`, and an explicit `ToArray()` for consumers that
need to keep it. Valid until the next `Process` on that book, stated in the type and in the
comment on `IOrderBook`. Every existing external caller breaks at compile time, which is the point
- this is a lifetime change, and it should be one the compiler makes you read. It also drops the
interface dispatch on the iteration everything downstream does.

**Recommended: C.** The package is at 0.7.0, the break is loud rather than silent, and the
retaining consumer - a journal - is precisely the one that should be copying anyway. The rule to
document, once, in `IOrderBook`: *a batch is valid until the next call on the book that produced
it; to keep it, copy it.*

The consequence to plan for is `Sequencer.AdvanceTo`, which today returns a list of `Dispatched`
each holding a book's events. Under C those references would all alias one buffer as soon as a
single `AdvanceTo` dispatches two actions. So `AdvanceTo` gains a push-based overload -
`AdvanceTo(DateTime, IDispatchSink)`, or an `Action<Dispatched>` where a cached delegate is
acceptable - which becomes the allocation-free path and the one `Replay` uses. The list-returning
overload stays, copies each batch, and is documented as the convenience path it already is.

## The plan

Each stage is independently shippable and ends green. The estimates are objects removed per action
on the common path; they are estimates because Stage 0 has not run yet.

### Stage 0 - make it measurable

No production change. Extend `OrderBookThroughputBenchmarks` with the cases the current pair does
not cover: a pre-open session (the auction quoting path), a replay through an `InstrumentGroup`
with market data attached (the producers and the channel), and per-operation benchmarks for rest,
cancel, and a sweep that trades. `[MemoryDiagnoser]` is already on, so allocated-bytes-per-op comes
free.

Then a coarse guard that runs where BenchmarkDotNet cannot: a test that replays a fixed trace,
brackets it with `GC.GetAllocatedBytesForCurrentThread()`, and asserts bytes-per-action below a
ceiling. It is not a benchmark and should not pretend to be - it exists so a regression fails CI
instead of being noticed a quarter later.

Record the baseline in this document.

### Stage 1 - free early-outs (~4 per trade, most of pre-open)

The cheapest work on the page, and none of it changes a contract.

- `GatherTriggeredStops`: `TryGetBest` on each stop ladder before enumerating. No stop in reach,
  no iterator.
- `TryQuoteIndicative`: return false immediately when the book is uncrossed - it already computes
  that, but only after materialising both ladders.
- `QueueInterruptionTicks`: index loop and an `is` test instead of `OfType<StatusChanged>()`.
- `Match`: hoist `pendingImmediateOrCancelStops` to a field, cleared per call.

### Stage 2 - event buffer ownership (~4 per action)

Option C above. `Handle`, `CreateOrder`, `UpdateOrder`, `CancelOrder`, `ResumeIfDue`,
`UpdateStatus`, `ExpireOrders` and the three `Reject*` helpers all stop returning lists and append
into the book's buffer instead. `Process` returns an `EventBatch` over it.

`Sequencer` gains its push overload; `Replay` and `LiveDriver` move onto it. Tests and samples
follow. `IOrderBook`'s comment gains the lifetime rule.

This is the largest diff in the plan and the one with the least cleverness in it - almost every
hunk is a `return new List<...> {x}` becoming an `Add(x)`.

### Stage 3 - the matching loop (~4 per action, ~3 per trade)

`Matcher.Run` stops being an iterator. Either a struct enumerator, or a `TryNext(out MatchOutcome)`
stepped by the caller - the second reads closer to what the loop actually is, given the comment on
`Run` already says the caller must apply each outcome before asking for the next.

`MatchOutcome` becomes a readonly struct with a `Kind` discriminator, replacing the four records.
`OrderBook.Apply` switches on `Kind` rather than pattern-matching on type. This costs some of the
current readability - deconstruction into named fields is genuinely nicer than reading
`outcome.Resting` and trusting `Kind` - and buys an allocation per trade, per self-match, per stop
election, per breach.

`Func<long, RestrictionBreach?>` goes: `Matcher.Run` takes an interface the book implements
(`ITradeRestrictionCheck.Check(long priceTicks, DateTime time)`) and is handed `this`, with `time`
passed as a parameter rather than captured. Display class and delegate both gone.

`IReadOnlyPriceLadder.EnumerateFromBest()` returns a struct enumerable rather than
`IEnumerable<...>`. Everything reading a ladder is internal, so this is a mechanical change with no
public surface: `GatherTriggeredStops`, `HasSufficientLiquidity`, and the auction's quoting pass.

`GatherTriggeredStops` also loses its `SortedDictionary` + `ToList` for a reusable buffer sorted in
place by sequence number.

### Stage 4 - market data producers (~25 per dispatch)

The biggest single number in the plan, and the one with behaviour to check.

`IDataProducer<T>.Process` stops returning `IList<T>` and writes into a caller-supplied
`List<MarketDataEvent>`. `InstrumentFeed.Collect` and its copy disappear; each producer's per-call
list disappears with it.

`LevelDataProducer` gets the `PriceLadder` treatment: levels indexed by tick in an array rather
than a `SortedDictionary<decimal, LevelState>` keyed on decimal, with the state a struct in that
array rather than a class per level. Snapshots build into a reused buffer and allocate one
right-sized array at the end instead of going through `Take().Select().ToList()`.

Then the behavioural part: **emit `LevelsDataEvent` only when the published depth actually
changed.** Most actions do not move the top ten, and today every one of them publishes a full
snapshot anyway. This is a real change to what a subscriber receives, so it needs its own pass
over the market data tests and a decision recorded here - a venue publishing an unchanged snapshot
per action is not obviously wrong, it is just expensive. If the answer is that the stream must stay
per-dispatch, the array and buffer work still stands and only the dedupe is dropped.

`MarketDataChannel.GroupBySymbol` gets a single-symbol path that calls straight through rather
than allocating an array to iterate; the grouping dictionary and order list for the multi-instrument
path become reused fields.

### Stage 5 - the auction and pro-rata quoting paths

`TryQuoteIndicative` rewritten as a single pass over the two ladders with reusable arrays and no
LINQ, computing cumulative depth from each end rather than re-summing every level for every
candidate price. This is a complexity fix as much as an allocation one: O(levels) instead of
O(levels²), on a path that runs per action for the whole pre-open session.

`ComputeAllocations` likewise: reused buffers, an in-place sort for the remainder distribution,
and no LINQ. Pro-rata pays this per level entered, so it matters most under the flow it was built
for.

Both are pure functions with dense test coverage, which is what makes rewriting them the safe kind
of change rather than the frightening kind.

### Stage 6 - the `Order` snapshot and the string ids

Left last because it is the only part with a real trade-off in it.

`ExchangeOrderId` is `SequenceNumber.ToString()` recomputed on every read, and it is read at least
once per event. Caching it on `InternalOrder`, invalidated wherever `SequenceNumber` changes
(`Update`, `Replenish`, `ConvertToLimit`), is free and removes a string per event. Same for the
trade id, which is `_nextTradeId.ToString()` per trade.

The `Order` snapshot is the fattest object in the stream - 21 members, one per event, two per
trade. Two ways out:

- **`Order` as a `readonly record struct`.** Removes a heap object per event; the fields land
  inside the event object that was going to be allocated anyway. `with`, deconstruction and value
  equality all survive. The cost is copy size at every boundary that passes an `Order` by value,
  and it stops being nullable - so this needs a sweep for `Order?` and for reference comparisons
  before it can be called safe.
- **Long ids on the events, strings only at the edge.** `ExchangeOrderId` and `TradeId` are
  `long`s that the engine converts to strings solely because the public records say `string`.
  Changing that is a bigger API decision than an allocation one, and belongs to whatever
  conversation 1.0 is.

Measure before doing either. If Stages 1-5 land and the remaining profile is dominated by the
events themselves, this is where the next round starts; if it is not, the API churn is not worth
buying.

## Not doing

**Pooling event objects.** Events escape to subscribers and to whatever journals them, and a pool
turns "keep this event" into a use-after-free with no compiler help. The batch contract in Stage 2
is already at the edge of what is reasonable to ask a consumer to know; pooling the events inside
it is past it.

**Touching `PriceLadder`.** It is already the thing the rest of this plan is trying to become.

**Struct events.** A polymorphic event stream is the shape of the API, and flattening it into a
tagged union to save one object per event would rewrite every consumer in the repository to save
less than Stage 4 saves on its own.

## How we will know

Determinism is the invariant: the same trace produces the same events, event for event, before and
after every stage. `DeterminismTests` already asserts it, and no stage here is allowed to change
what the book emits - Stage 4's level dedupe is the single exception, and it changes what the
*market data* emits, not the book.

Each stage ends with the Stage 0 benchmarks re-run and the numbers appended to a table in this
document, so the plan accumulates its own evidence. The allocation ceiling test tightens as stages
land; a stage that does not move it is a stage that was wrong about where the garbage was.
