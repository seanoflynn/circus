# Circus

A financial exchange simulator.

## Dependencies

[.NET 10](https://dotnet.microsoft.com/download) is required. There are no other dependencies.

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

Safety features
- [x] Banding
- [x] Volatility interruptions (dynamic, static and extended ranges)
- [x] Velocity logic (too far, too fast over a rolling window)
- [x] Daily price limits (static, session-long limit up/down)
- [x] Circuit breakers (levelled, widest breached level wins)
- [ ] Stop price logic
- [x] Self-match prevention

Matching algorithms
- [x] FIFO
- [x] Open auction
- [ ] Allocation
- [ ] Pro-rata
