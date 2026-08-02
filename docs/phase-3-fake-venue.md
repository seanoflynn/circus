# Phase 3 — the fake venue

The second of the two uses in `direction.md`: an in-process exchange a live trading stack can be
tested against, with real acks, fills, rejects and market data, and the venue behaviour that is
impossible to provoke on a real one.

Mostly assembly of parts that already exist, with one piece of genuine design in it. Taking that
piece first, because it is the one that decides whether the rest is worth having.

---

## The participant view, which does not exist today

A book's events are the whole venue's events. Every participant's orders are in the one stream,
and nothing anywhere filters them - there is not a single `CompanyId` comparison in `src`. A
stack under test is one participant, and what it should see is its own order lifecycle plus the
public feed, which is not what any current type hands out.

Filtering by `CompanyId` looks like a morning's work and is not, because of one event:

```csharp
public record OrdersMatched(string Symbol, DateTime Time, decimal Price, int Quantity,
    IList<FillOrderConfirmed> Fills) : OrderBookEvent(Symbol, Time);
```

`OrdersMatched` carries no `CompanyId` of its own and holds a fill for *both* sides of the
trade. So the two obvious filters are both wrong:

- Keep events whose `CompanyId` matches, and a participant never sees its own fills at all -
  `OrdersMatched` has no company to match on, so it is dropped whole.
- Keep any `OrdersMatched` holding one of the participant's fills, and the counterparty's
  `FillOrderConfirmed` goes out with it. That carries their whole `Order` record: their company,
  their client order id, their remaining and displayed quantities. A venue that leaked that
  would be teaching the stack under test to rely on something no real venue sends.

So the view has to rewrite the event rather than filter it - reducing `OrdersMatched.Fills` to
the participant's own - and the same question has to be asked of every composite event added
later. That is the design work in this phase, and it belongs in one place rather than in each
consumer's LINQ.

What a participant sees, then, is two streams, exactly as at a real venue:

- **Private**: its own `OrderEvent`s - confirms, rejects, fills, expiries - and nothing else's.
- **Public**: `MarketDataChannel`, unchanged and unfiltered. It is already public by
  construction: every producer derives from events without naming a participant, and depth is
  already reported as `DisplayedQuantity` so an iceberg's reserve never reaches it.

A test asserting "my order was rejected for `PriceOutsideBands`" reads the first. A test
asserting the market moved reads the second. Conflating them is what makes fake venues teach
stacks to depend on information they will not have in production.

---

## Scenario injection, which is nearly free

The interesting venue states - a volatility interruption, a circuit breaker, limit up - are
reachable today only by constructing a price sequence that trips them. An OMS team wants to say
"go limit up now" and get on with the test.

The seam for it already exists and is worth noticing before building anything:

```csharp
internal OrderBook(Instrument instrument, IReadOnlyList<IPriceRestriction> priceRestrictions)
```

`IPriceRestriction` is already the complete vocabulary of what a restriction can do to a book -
refuse an entry with a named `OrderRejectedReason`, block a print, pause or halt, for a stated
duration, resuming under its own conditions. A scenario is nothing more than a restriction that
answers on command instead of on price. Nothing in `OrderBook` needs to change to support one.

What that buys, with no new machinery in the engine:

| Scenario | How |
| --- | --- |
| Reject the next entry, any reason | Scripted `OrderEntry` restriction |
| Limit up / limit down | Scripted `Trade` restriction returning `Block`, which is what makes the book emit `LimitStateChanged` while staying open |
| Volatility pause, timed resume | Scripted `Trade` restriction returning `Pause` with a `ResumeAfter` |
| Circuit breaker halt | The same returning `Halt` |
| Interruption that extends rather than resolving | `AllowsResumption` returning false |

The rest needs no injection at all, because it is already an action or a schedule: an outright
halt or pause (`HaltTrading`, `PauseTrading`), a session boundary, a day roll, order expiry at
the close. Priority loss on a modify needs nothing but a modify.

**The one decision here** is how the harness reaches `IPriceRestriction`, which is `internal`
and deliberately so - `Circus.csproj` grants internals to `Circus.Tests` alone. Either add the
harness assembly to that list, or promote the interface to public API. I would add to the list:
the harness can expose a public `Scenario` vocabulary of its own and implement the internal
interface behind it, which keeps a genuinely internal seam internal and keeps Circus from owing
compatibility on a type whose shape is still moving.

---

## The shape of it

A new project, `src/Circus.TestKit`, packable, holding a facade over the parts that exist:

```csharp
var venue = new Venue(start: new DateTime(2026, 1, 5, 8, 0, 0, DateTimeKind.Utc));
venue.Add(gold, schedule);

var me = venue.Participant("MyFirm");

venue.AdvanceTo("08:30");                       // pre-open arrives from the schedule
me.Submit(new CreateLimitOrder { ... });
venue.AdvanceTo("09:00");                       // the open, and its auction print

me.AssertFilled("B1", quantity: 3, price: 1000);

venue.Scenario.LimitUp(at: 1010);
me.AssertRejected("B2", OrderRejectedReason.BeyondDailyPriceLimit);
```

Underneath: `InstrumentGroup` for the books and the channel, `LiveDriver` over a `ManualClock`
for time, a participant view per company, and a recorder over the dispatch stream.

`AdvanceTo` is the whole of time control - set the clock, tick the driver, publish what came
back. Worth one test of its own: advancing thirty minutes in a single jump must still resume an
interruption whose deadline fell inside it, because the sequencer queues that deadline as a tick
and `AdvanceTo` drains everything at or before the target rather than only what was queued on
entry. That is already how it behaves; a harness that made it stop behaving that way would be
worse than useless.

`ManualClock` rather than `SystemClock` is what makes a test deterministic, and it is the only
difference from a production host - which is the point worth making in the sample.

---

## Recording, and why golden files actually work here

Everything downstream of the actions is a pure function of them, so a recorded dispatch stream
is a complete and reproducible description of a session. That makes golden-file testing
genuinely sound rather than flaky-by-construction: a diff in the file is a change in venue
behaviour, not a change in timing.

Two pieces:

- **`VenueRecorder`** over the `Dispatched` stream, and a way to write it back out.
- **A stable rendering.** Records' generated `ToString` renders a list as its type name, which
  `samples/Circus.Examples/Display.cs` already works around for `LevelsDataEvent`. A golden file
  needs that done properly and in one place - and the harness and the samples should share it
  rather than each carrying a copy.

---

## Order of work

**3a. `Venue`, time control, participant views.** The foundation, and where the design risk is.
Nothing else is worth building first, and the participant view is the piece to get right before
anyone depends on its shape.

**3b. Scenario injection.** Cheap once 3a exists, given the seam is already there. Mostly a
question of what vocabulary to expose.

**3c. Recording, golden files, assertion helpers.** Polish, and the part most easily changed
later.

**3d. A sample.** `samples/Circus.Examples` should grow one showing a stack-under-test shape -
submit, advance, assert - alongside the four already there. CI runs the samples now, so it stays
honest.

---

## What this is not, and should not pretend to be

Worth stating plainly, because a fake venue that quietly implies more than it models is how a
stack passes its tests and fails in production.

- **No transport.** No FIX, no binary protocol, no sockets. The harness is in-process, so a
  stack's encoding, session and reconnect layers are not exercised by it. This is the one thing
  most likely to be assumed and is worth saying in the README of the package itself.
- **No disconnects, gaps or resends**, which follows from the above: they are transport
  behaviour, and the market data producers cannot resync after a missed event by design.
- **No credit, risk, position or margin checks.** Circus models a matching engine, not a
  clearing house.
- **Single-threaded**, like everything else here. A stack submitting from several threads has to
  marshal onto the venue's thread, and the harness should make that obvious rather than papering
  over it with a lock.

---

## Open question

Whether the stack under test is in-process .NET, or talks a wire protocol.

Everything above is the in-process core and is needed either way - a transport shim would sit on
top of exactly these parts. But if the point is to exercise a FIX gateway end to end, that shim
is a substantial piece of work in its own right and belongs in the plan explicitly rather than
being discovered halfway through 3a.
