# Circus

A financial exchange simulator.

## Dependencies

[.NET 10](https://dotnet.microsoft.com/download) is required. There are no other dependencies.

## How it works

Actions in, events out. An `OrderBook` reads no clock and consults nothing ambient: every action
carries the instant it happened at, so the same actions always produce the same events, and a
journal of those actions is enough to rebuild a book by replaying it.

Everything a consumer knows is derived from that event stream rather than queried back out of a
book. Market data producers turn events into what a subscriber sees - depth, order-by-order,
trades, indicative quotes, instrument status - so the same code that publishes a live feed
rebuilds one from a recorded trace.

Where time comes from is the only thing that differs between running and replaying.
`LiveDriver` stamps arriving actions from a clock; `Replay` takes the instants already on a
recorded trace. Both feed a `Sequencer`, one queue in front of every book, whose dispatch order
is the venue's order of events.

```
                    ┌──────────────┐
   live ─ clock ──▶ │              │        ┌────────┐      ┌──────────────┐
                    │  Sequencer   │ ──────▶│  Books │ ────▶│  Market data │──▶ subscribers
 replay ─ trace ──▶ │              │        └────────┘      │   channel    │
                    └──────────────┘                        └──────────────┘
```

Agents are subscribers that send orders back. An agent knows the market because it subscribed to
the feed and knows what it is holding because it saw its own confirms and fills, so a venue with
agents in it is the same venue with the return path joined up - no component in the diagram
changes, and there is no second book anywhere modelling the first.

That gives two things a venue on its own does not. Point a swarm at a live venue and there is
something to trade against, arriving through the same driver a gateway would use. Record what a
seeded swarm sent and there is a trace: reproducible from its seed, replayable into a fresh venue,
and the input the benchmarks and the replay tests run on.

See `samples/Circus.Examples` for each of these; `dotnet run --project samples/Circus.Examples`
runs them all.

## Features

Order types
- [x] Limit orders
- [x] Market orders
- [x] Stop market orders
- [x] Stop limit orders
- [x] Market limit orders

Order properties
- [x] Min quantity
- [x] Max visible quantity

Time in force/order validity
- [x] Day orders
- [x] GTC orders
- [x] FAK/FOK orders
- [x] GTD orders

Sessions
- [x] Time provider
- [x] Sessions
- [x] Multiple sessions per day
- [ ] Market statistics

Market data
- [x] Trades
- [x] Price/qty/count for x levels
- [x] All order updates
- [x] Indicative open
- [x] Instrument status (trading state, why, when it resumes, limit up/down)

Safety features
- [x] Banding
- [x] Volatility interruptions (dynamic, static and extended ranges)
- [x] Velocity logic (too far, too fast over a rolling window)
- [x] Daily price limits (static, session-long limit up/down)
- [x] Circuit breakers (levelled, widest breached level wins)
- [ ] Stop price logic
- [x] Self-match prevention

Matching algorithms, selected per instrument
- [x] FIFO (price-time)
- [x] Open auction
- [x] Pro-rata
- [ ] Allocation (top-order priority, split FIFO/pro-rata)

Agents
- [x] Participants that read the feed and their own events, and hold no book
- [x] Seeded liquidity agents (depth, spacing, size, aggression, sweep, churn, position limit)
- [x] Recorded traces, reproducible from a seed and replayable
- [ ] Quoting into an auction rather than only continuous trading
