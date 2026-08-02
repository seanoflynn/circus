# Direction

Circus is being pointed at two uses, and everything below is judged against them. Work that
serves neither is not on this list, however interesting it is.

**A. Counterfactual backtesting.** Replay a venue's recorded order flow, interleave our own
orders into it, and find out what they would have done — real queue position, real partial
fills, real band and limit rejections. Ultimately with a twin book, so the divergence between
the perturbed run and reality is measured rather than assumed.

**B. A deterministic fake venue.** An in-process exchange the live trading stack can be tested
against: real acks, fills, rejects and market data, and the exchange behaviour that is
impossible to provoke on a real venue — halts, limit locks, priority loss on modify, GTD expiry
at a session boundary.

The property both rest on is the one the library already has: a book is a pure function of the
stamped actions it is handed, and every derived view is a function of the events that come back
out. Nothing below should be allowed to weaken that.

---

## Where we actually are

B is mostly packaging. `OrderBook`, `LiveDriver`, `ManualClock`, `InstrumentGroup` and the
market data producers already do the work; what is missing is a way to ask for a scenario
without hand-building the price sequence that trips it, and somewhere to put the harness.

A is where the engineering is. Today nothing can get real market data into the engine at all:
the only things that produce actions are hand-written ones and `OrderFlowSimulator`. Every
producer must exist before its book's first action and can never resync, so a capture that
starts mid-session has nowhere to start from. And there is no way to tell whether a replay
reproduced the venue it came from, which is the question everything else depends on.

---

## Phase 0 — clear the ground

Small, and worth doing first because the samples currently teach the wrong architecture to
anyone arriving at the repo, including us.

### Samples

Not one sample uses `Sequencer`, `Replay`, `InstrumentGroup` or `OrderFlowSimulator`. They are a
snapshot of the design as it stood before the sequencing work, and `Program.cs` only ever calls
`OrderBookExample.TestExample()` — so `BackTestExample`, `LiveExample` and
`MarketDataProducerExample.Run` are unreferenced, and nothing but the compiler has looked at
them in some time.

- **Delete `OrderBookExample.LiveExample`.** It starts a `Task.Run` that drives the book's
  schedule on a background thread while the caller submits orders to the same book from the
  main one. The book is single-threaded by construction; the sample's own comment says the
  driving "needs to happen on same thread as book is updated" and then the code does the
  opposite. `LiveDriver` is the answer to what this was reaching for. Replace it with a sample
  built on `LiveDriver` + `ManualClock`.

- **Delete `OrderBookExample.BackTestExample`.** This is the sample closest to what we now want
  and the most misleading one we have. It hand-rolls a `DriveTo` walk of the schedule that
  `Sequencer` exists to do, never touches `Replay`, and its loop stamps all hundred orders at
  the same instant and puts them all on the same side at the same price, so nothing ever
  trades. Rewrite as a `Replay` over an `OrderFlowSimulator` trace.

- **Delete the `DriveTo` helper** with them. `Sequencer.Add` queues a book's transitions from
  its schedule; nothing should be walking one by hand any more.

- **Rewrite `MarketDataProducerExample` on `InstrumentGroup`.** It is the most current sample we
  have, but it wires a channel to its books by hand — which is the wiring `InstrumentGroup`
  exists so that we cannot get wrong — and drives them off a `SystemClock`, so its output
  differs every run. A sample about a deterministic engine should print the same thing twice.

- **Make the samples runnable and deterministic**, each reachable from `Program.cs` by name, and
  smoke-run them in CI. A sample nothing executes is a sample that rots, which is how we got
  here.

### Pro-rata is implemented but unreachable

`ProRataMatchingAlgorithm` landed in #76 with tests, and `OrderBook`'s phase table still
hardcodes `PriceTimeMatchingAlgorithm` for the open phase. Until the algorithm is selectable per
instrument it is tested dead code — and pro-rata markets are exactly the ones where a naive fill
assumption is most wrong, so this matters to A rather than being housekeeping.

### README

Says pro-rata is not done when it is, and describes no part of the sequencing or market data
architecture — a reader learns about order types and safety features and nothing about the
action/event model the library is actually built on.

---

## Phase 1 — get real data in, and prove it went in correctly

The foundation for A. Nothing after this is worth anything without it.

**1. An ingest contract: venue messages to `OrderBookAction`.** A `Circus.Ingest` project holding
the mapping, with no venue-specific parsing in the core. Two details the mapping has to get
right, both of which are quiet if wrong: build the trace from add/modify/cancel only and let
the matcher generate the prints, because a venue's trade messages are its matcher's *output*
and feeding them back in double-counts; and leave `SelfMatchPrevention` null on historical
orders, since it is opt-in and a shared `CompanyId` across the tape would manufacture
self-match cancels that never happened.

**2. Initialising a book from a snapshot.** Captures start mid-session and have gaps. Today a
book and its producers must be present from the first action of the day and can never resync —
`IDataProducer` says so explicitly. We need a way to seed a book with resting orders as of an
instant, and to seed the producers to match, without replaying a day to get there. This is the
largest structural gap in the library and everything about A is limited by it.

**3. A reconciliation harness.** Replay a capture with no orders of ours in it and diff the
`OrdersMatched` the engine generates against the trades the venue recorded. Report match rate,
first divergence, and a classification of what caused it. The first runs will not reconcile —
an MBO feed shows neither iceberg reserve nor implieds, so the engine can only match what was
visible — and knowing the size of that gap is the point. Until this exists, a counterfactual
result is a number with no error bar.

---

## Phase 2 — the twin, and honest fills

**4. A twin session.** One reference book fed the trace alone, one perturbed book fed the trace
plus our actions, off the same stream. The reference is ground truth; the difference is what we
did. Report: first divergence instant, rejects against historical company ids counted by reason,
phantom depth (perturbed minus reference), and the count of orders cancelled with
`UpdatedQuantityLowerThanFilledQuantity`.

That last one is worth naming because it is where a partial fill of ours turns a legal size
reduction into a cancellation. Reality reduces an untouched order from 10 to 2; we had taken 5
of it, so 2 is at or below what is filled and `OrderBook` cancels the whole order. Liquidity
that should have stayed disappears, and nothing about it looks like an error.

The other leak runs the other way and is quieter still: rest ahead of a historical order, take
the fill that was theirs, and their order survives in our book though it was filled in reality —
so the tape holds no further action for it, nothing ever removes it, and it sits there as depth
that never existed until the close expires it. Note that this is the *passive* case, which is
the one that feels safe.

**5. Delta translation.** The fix for both, and the "super realistic" part of the fill
simulation. Rather than applying the trace's absolute quantities to the perturbed book, apply
each action to the reference first, observe what it did there, and apply that effect to the
perturbed book. Spurious cancellations stop; an order dying in the reference takes its phantom
residue with it. Only possible because there is a real matcher on both sides, which is the whole
reason to do this here rather than with a book reconstructor.

**6. A strategy drive loop with latency.** A host that hands a strategy each `Dispatched` and the
channel messages behind it, and lets it enqueue actions at `event time + its latency`.
`Sequencer.Submit` already refuses anything stamped behind logical now, so lookahead is
structurally impossible rather than merely discouraged — make that an explicit guarantee with a
test that names it, because it is one of the better properties we have and it is currently an
accident of the queue's design.

---

## Phase 3 — the fake venue

Mostly assembly of parts that exist.

**7. A harness package.** Build a venue from configuration, drive it with `LiveDriver` over a
`ManualClock` so a test owns the clock and stays deterministic, submit orders, assert on what
came back.

**8. Scenario injection.** `HaltTrading` and `PauseTrading` can already be sent as actions, but
the interesting states — a volatility interruption, a circuit breaker, limit up — are reachable
only by constructing a price sequence that trips them. A stack under test wants to say "go limit
up at 10:15" and get on with it. This is the single change that decides whether B is genuinely
useful to whoever owns the OMS.

**9. Assertion helpers.** Capture the dispatch stream, assert an order was rejected for a given
reason, golden-file an event stream so a change in venue behaviour shows up as a diff.

---

## Phase 4 — scale

Only once A works end to end, and only against measurements.

**10. Allocation per action.** Every action returns a freshly allocated
`IReadOnlyList<OrderBookEvent>`. A day of one liquid future is many millions of messages and
this will dominate. The benchmark harness to measure it already exists; measure before changing
anything.

---

## What this does not make Circus

Worth writing down so it is not rediscovered in an argument later.

The tape does not react to us. No participant fades our quote, no one races us, nobody is
adversely selected by our presence. That is inherent to replaying a recording and no amount of
bookkeeping in phase 2 fixes it — it is why counterfactual results decay with horizon and with
size, and why the divergence report in phase 2 is a headline number rather than a diagnostic.

For a passive strategy at small size there is a cheaper and stricter alternative worth keeping
in view: never insert into the book at all, track a notional queue position from the L3 stream,
and infer fills from prints sweeping through it. Zero divergence by construction, because
nothing is perturbed. It cannot model our own impact and cannot honestly model an aggressive
order, but it needs only the reconstruction from phase 1 and none of phase 2.
