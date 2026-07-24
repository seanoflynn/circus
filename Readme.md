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
- [ ] Market statistics

Market data
- [x] Trades
- [x] Price/qty/count for x levels
- [ ] All order updates
- [x] Indicative open

Safety features
- [x] Banding
- [ ] Daily price limits (static, session-long limit up/down)
- [ ] Circuit breakers
- [ ] Stop & velocity logic
- [x] Self-match prevention

Matching algorithms
- [x] FIFO
- [x] Open auction
- [ ] Allocation
- [ ] Pro-rata
