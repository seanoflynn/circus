# Replacing the simulator and its shadow book with agents

A plan, mostly carried out. Steps 1 to 4 have landed: `Circus.Agents` exists, `LiquidityAgent`
generates flow, `AgentTrace` records it, and `Circus.Simulator` is gone. Step 5 - a sample of a
live venue with agents quoting into it - is outstanding.

What follows is the plan as written, not a description of what was built, and the two differ in
places: `AgentSwarm` attaches to a venue rather than building one, and aggression split into a
probability and a sweep depth. Once step 5 lands this document should either be deleted or
rewritten as a design note on why participants hold no book - the reasoning is worth keeping, the
step list is not.

## Where we are

`Circus.Simulator` generates a trace of actions by rolling dice and, to keep those dice honest,
running a private `OrderBook` per instrument plus a one-deep `LevelDataProducer` over its events.
The shadow book exists for one reason: so a generated cancel or update names an order that is
still resting rather than a random id.

That works, and it has four costs.

**A second engine that nobody meant to have.** The shadow book is a real `OrderBook` used as a
bookkeeping device. It is opened once with `OpenTrading` and never moved through a session again -
no schedule, no restrictions, no auction. So flow is generated against a market that behaves
differently to the one the trace is later replayed into, and the simulator cannot react to
anything the real venue does: a halt, a limit-locked market, a band breach, a rejection.

**No participants.** `NextCompanyId()` mints a fresh company for every order, so the trace has as
many companies as orders. Self-match prevention is unreachable by construction. So is inventory,
position, and any behaviour that depends on what the same trader did a moment ago.

**Open loop.** `Generate(n)` produces a trace up front and hands it over. There is no way to point
it at a running venue and trade against it, which is half of what the request asks for.

**Duplicated tracking.** `BookState.Track` already does exactly the private-order bookkeeping a
participant would do - follow your own confirms, fills, cancels and expiries, and know what you are
holding. It just does it beside a shadow book instead of instead of one.

## Where we are going

An agent is a participant. It knows the market because it subscribed to the feed, and it knows what
it is holding because it saw its own confirms and fills. That is the split the codebase already
draws - `FillOrderConfirmed` carries one side's `CompanyId` precisely so a private view is a filter
over the event stream and a public print is derived separately - and following it removes the
shadow book rather than replacing it with something else.

The engine does not change. Agents add no new source of time (they are stamped by `LiveDriver`, the
only thing that reads a clock), no new ordering authority (the `Sequencer` still decides), and no
second matching engine (there isn't one any more).

## Shape

New project `src/Circus.Agents`, referencing `Circus`, not packable - the same standing
`Circus.Simulator` has today. `Circus.Simulator` is deleted at the end of the sequence below.

### `IAgent`

```csharp
public interface IAgent
{
    string CompanyId { get; }
    IReadOnlyList<string> Symbols { get; }

    // The public feed, for the instruments this agent trades.
    void OnMarketData(MarketDataEvent data);

    // Its own order events - the ones carrying its CompanyId, and only those.
    void OnOwnEvent(OrderBookEvent ev);

    // Unstamped: an agent does not get to say when its order reached the exchange.
    IReadOnlyList<OrderBookAction> Act(DateTime now);
}
```

Observing and acting are separate calls rather than one `OnEvent` that may return actions. A whole
dispatch's worth of events is delivered to every agent before any of them acts, so what an agent
decides is a function of the venue state at the tick boundary and not of how many events happened
to arrive in one batch. That is what keeps a run reproducible.

`Act` returns actions without a `Time`; the harness stamps them through `LiveDriver.Submit`, which
already does exactly this for a gateway.

### `OrderTracker`

The surviving half of today's `BookState`, with the shadow book taken out: live orders keyed by
client order id, carrying side, price, quantity, trigger price; renamed across the chain of ids an
update mints; dropped on cancel, expiry or a fill that empties them. Plus position and average
price from fills, which the simulator has no way to know today.

Every field of it comes from an event the venue emitted. Nothing in it processes an action, so it
cannot disagree with the book - and if it ever does, that is a bug in the venue's events worth
finding, which is a nice property for a test harness to have.

`ClientOrderId`s are minted per agent (`$"{CompanyId}-{n}"`, keeping inside the book's 20-character
limit). Orders are keyed `(CompanyId, ClientOrderId)` in `OrderBook`, so per-agent counters are
enough - no shared counter across the venue, which is what the anonymous ids force today.

### `MarketView`

Top of book, last trade, status and limit state per symbol, maintained from `LevelsDataEvent`,
`TradeDataEvent` and `InstrumentStatusDataEvent`. This is what the shadow book's one-deep
`LevelDataProducer` was for, except it is now fed by the venue's own feed - so agents exercise the
market data path rather than running a private copy of it.

### `LiquidityAgent` and `LiquidityAgentOptions`

The seeded workhorse: quotes a ladder each side and lets it decay, with everything the request asks
for as a parameter.

```csharp
public record LiquidityAgentOptions(
    decimal ReferencePrice = 1000m,     // used only while the book has no prices of its own
    int Depth = 3,                      // levels quoted per side
    int LevelSpacingTicks = 1,
    int SizeMin = 1,
    int SizeMax = 10,
    int MaxLiveOrders = 20,
    double ActProbability = 0.5,        // chance of doing anything at all on a given tick
    double Aggression = 0.05,           // chance an order is priced marketable, and how deep it sweeps
    double ReplaceProbability = 0.2,    // reprice rather than leave resting
    double CancelProbability = 0.1,
    int? MaxPosition = null,            // skew away from the side that would grow inventory
    int? MaxVisibleQuantity = null      // set to exercise icebergs
);
```

Each `Act`: if the instrument is not open, do nothing (a pre-open quoting flag can come later).
Otherwise take a reference from the `MarketView` - mid, else last trade, else `ReferencePrice` -
build the ladder it wants, compare against what `OrderTracker` says it is holding, and emit the
cancels, updates and creates that close the gap, gated by the per-tick probabilities so the flow is
not a metronome. `Aggression` is the one dial that crosses the spread; at 0 the agent is pure
liquidity.

A `TakerAgent` is not a separate type to begin with - aggression on this one covers it. If the
behaviours diverge enough later, split then.

### `AgentSwarm`

The population of participants, and the only new moving part:

```csharp
Tick(now):
    dispatched = driver.Tick()                  // LiveDriver.Tick -> Sequencer.AdvanceTo(clock)
    for each dispatch:
        route events carrying a CompanyId            -> OnOwnEvent, to that agent
        route channel.Publish(events)  by symbol     -> OnMarketData
    for each agent, in registration order:
        driver.Submit(action) for each action from agent.Act(now)
```

It builds no venue. An `InstrumentGroup` and the `LiveDriver` pumping its sequencer are wired by
whoever owns the venue and handed in - which is what a host does anyway, and is what lets the same
group carry restrictions, matching algorithms and instruments the agents know nothing about,
alongside flow from gateways that are not agents at all. The swarm is the participants and the
return path to them, and nothing else.

It reads no clock either: `now` is passed into `Tick`, the way a book is told the instant an
action happened rather than looking it up. What the agents send is stamped by the driver on its
own reading.

Agent orders submitted on a tick dispatch on the next one - one tick of latency, which is both
realistic and what makes the feedback loop well founded rather than re-entrant.

Two ways to drive it, mirroring the `LiveDriver` / `Replay` symmetry that already exists:

- **Deterministic.** A `ManualClock` and a fixed step, ticked N times. Same seed, same run, every
  time. This is what tests and recorded traces use.
- **Live.** A `SystemClock`, `Tick()` pumped on a timer, and the driver left open so a human or a
  test client can submit into the same venue and trade against the agents. This is the "test
  trading against" case, and it needs no new code - only a different clock, exactly as the
  engine's own live/replay split does.

### `AgentTrace`

```csharp
public static IReadOnlyList<OrderBookAction> Record(
    IReadOnlyList<Instrument> instruments, int actionCount, int? seed = null, ...);
```

Wires a venue, runs a deterministic swarm at it, and records every action submitted, stopping
once `actionCount` have been captured. Returns the type today's consumers already take, so the
migration below is a one-line change in each of them, and the recorded trace replays into a fresh
venue to the same market data - which the shadow book could never quite promise.

One thing to be explicit about: a recorded trace reproduces the run only against a venue configured
like the one that recorded it. `Record` opens its books for the whole run by default, matching the
`OpenThroughout()` schedule every consumer already uses; a trace recorded against a schedule and
replayed into a different one will mostly produce rejections. Today's simulator hides this by
having no schedule at all.

## Migration

| Consumer | Change |
| --- | --- |
| `tests/.../Sequencing/ReplayTests.cs` (x2) | `new OrderFlowSimulator(...).Generate(400)` → `AgentTrace.Record(..., 400)` |
| `tests/.../Sequencing/InstrumentGroupTests.cs` | same, one call |
| `tests/.../MarketData/ChannelFromDispatchTests.cs` (x3) | same |
| `benchmarks/.../OrderBookThroughputBenchmarks.cs` | same, in `GlobalSetup` |
| `samples/.../ReplayExample.cs` | same, plus a comment refresh - the stand-in for a capture is now an agent run |
| `Circus.sln`, 3 `.csproj` references | `Circus.Simulator` → `Circus.Agents` |
| `README.md` | agents in the feature list; a line in "How it works" about participants being feed consumers |

Two consequences worth stating rather than discovering:

- **The benchmark numbers move.** Different flow, different book shapes, different fill rates.
  There is no committed baseline artifact to update, but the headline number will not be comparable
  across this change. Setup cost also rises - recording N actions now runs a real book and its
  producers - though that is `[GlobalSetup]` and outside what is measured.
- **Existing seeds mean nothing afterwards.** `seed: 21` is a different trace. Any test asserting a
  shape rather than a specific outcome survives; none of the current ones assert an outcome.

## Sequence

Each step compiles and leaves the suite green.

1. **`Circus.Agents` with the plumbing.** `IAgent`, `OrderTracker`, `MarketView`, `AgentSwarm`, and
   a trivial scripted agent to test the harness with. The simulator stays where it is.
2. **`LiquidityAgent` and its options**, with behavioural tests per parameter.
3. **`AgentTrace.Record`**, and switch the five trace consumers over. The simulator is now unused.
4. **Delete `Circus.Simulator`** and `OrderFlowSimulatorTests`; update the solution and README.
5. **`AgentSwarmExample`** in the samples: agents quoting into a live venue while a manual order
   trades through them, printed as a subscriber would see it.

## Tests worth having

Beyond porting what `OrderFlowSimulatorTests` pins today (a seed reproduces a run, different seeds
diverge, several instruments interleave, ids are unique, time runs forward):

- **Nothing an agent sends is rejected for bad bookkeeping.** Assert zero `CancelOrderRejected` /
  `UpdateOrderRejected` naming an order not in the book, across a long run. This is the property
  the shadow book existed to guarantee, now checked against the real venue instead of assumed.
- **A recorded trace replays to identical market data.** The end-to-end version of the reproducibility
  claim.
- **Agents fall silent when the book is not open**, and resume after a halt or a pause resolves -
  behaviour the simulator cannot express.
- **Parameters do what they say.** Higher `Aggression` prints more; higher `Depth` rests more
  levels; `MaxPosition` bounds inventory; `ActProbability` scales action count.
- **Two agents, one company id.** Self-match prevention becomes reachable for the first time.

## Decisions taken, worth challenging

- **Agents see market data, not raw book events, for the public view.** It is the real subscriber
  path and it exercises the producers. The cost is that an agent's view is capped at the feed's
  depth, which is also true of a real participant.
- **Several actions per tick, all stamped at the tick's instant.** The sequencer's counter breaks
  ties deterministically, so nothing is lost. The alternative - stepping the clock per action to
  give each its own instant - buys distinct timestamps and costs the "everything at this instant
  was decided from the same view" property.
- **Agents live outside `Circus`.** The engine consults nothing ambient; a participant is not part
  of it.
- **One agent type to start.** Aggression covers the taker case; splitting on speculation is how
  you end up with three agents that share 90% of their code.
