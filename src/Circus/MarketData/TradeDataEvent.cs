namespace Circus.MarketData;

// One trade, as a venue broadcasts it - CME's TradeSummary, Eurex's EMDI print. One message per
// trade rather than per fill, so an aggressor sweeping two resting orders prints twice and not
// four times.
//
// TradeId is the same id the two fills of the trade carry on the by-order feed, which is what
// makes the two products joinable. A subscriber holding both otherwise has a print at a price and
// a quantity, and some order events at the same instant, and no way to say which fills made which
// print - a sweep at one price across two resting orders looks identical to two unrelated trades.
// It is not the id a participant knows its own fill by either: that arrives privately on
// FillOrderConfirmed, which carries this same id, so a participant can find its own execution
// inside the public print.
public record TradeDataEvent(string Symbol, DateTime Time, string TradeId, decimal Price, int Quantity)
    : MarketDataEvent(Symbol, Time);
