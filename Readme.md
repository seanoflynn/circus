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

Order properties
- [ ] Min quantity
- [ ] Max visible quantity

Time in force/order validity
- [x] Day orders
- [x] GTC orders
- [x] FAK/FOK orders
- [ ] GTD orders

Sessions
- [x] Time provider
- [x] Sessions
- [ ] Market statistics

Market data
- [x] Trades
- [x] Price/qty/count for x levels
- [ ] All order updates
- [ ] Indicative open

Safety features
- [ ] Banding
- [ ] Limits
- [ ] Circuit breakers
- [ ] Stop & velocity logic
- [ ] Self-match prevention

Matching algorithms
- [x] FIFO
- [ ] Open auction
- [ ] Allocation
- [ ] Pro-rata
