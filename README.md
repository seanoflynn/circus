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

A book's events split in two. The public half is what a venue broadcasts and carries no client
identity; the private half is what a participant is told about its own orders. A channel sees
only the first.

What a channel publishes is configuration rather than code. Each one declares which instruments
it carries, which products about them, how deep its by-price products run, and how often it
restates itself, so a group can wear CME's shape - one channel per product group carrying
everything - or Eurex's, with the same book on an order-by-order interface and a netted depth one.
Each channel numbers its own messages, and each publishes an incremental stream alongside a
snapshot stream a subscriber uses to join mid-session or recover from a gap.

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
- [x] Overnight sessions (spanning midnight, dated by trading day rather than by the clock)
- [ ] Market statistics

Market data
- [x] Trades
- [x] Market by price (price/qty/count per level, price-keyed so each change applies on its own)
- [x] Market by order (every resting order, with fills paired by trade id)
- [x] Indicative open
- [x] Instrument status (trading state, why, when it resumes, limit up/down)
- [x] Public/private split (a feed never sees a client's own confirms)
- [x] Snapshot stream per channel, for joining mid-session and recovering from a gap
- [x] Channels configured per group: products, depth and snapshot cadence each
- [ ] End-of-event markers
- [ ] Instrument definition messages

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
