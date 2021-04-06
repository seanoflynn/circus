# Circus

A financial exchange simulator.

## Dependencies

[.NET 5](https://dotnet.microsoft.com/download) is required. There are no other dependencies.

## To Do

Priority
- [x] Sessions
- [ ] Simulator
- [ ] Stop orders

## Features

Order types
- [x] Limit orders
- [x] Market orders
- [ ] Market limit orders
- [ ] Stop market orders
- [ ] Stop limit orders
- [ ] OCO orders

Order properties
- [ ] Min quantity
- [ ] Max visible quantity

Time in force/order validity
- [x] Day orders
- [x] GTC orders
- [ ] GTD orders
- [ ] FAK/FOK orders

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

Contract types
- [ ] Futures (expiry)
- [ ] Calendar/spread contracts (implied pricing)

## Examples

