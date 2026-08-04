namespace Circus.MarketData;

// One change to one order in the working book, as carried inside a by-order message.
//
// ExchangeOrderId only - never CompanyId/ClientOrderId, which identify the originating client and
// must not be broadcast on a public feed.
//
// Quantity is the order's displayed size for Added and Modified, what left the book for Removed,
// and what traded for Filled.
//
// TradeId is set only on Filled and null everywhere else. A trade produces two of these, one per
// side, sharing an id - the same pairing FillOrderConfirmed carries privately. Without it a
// consumer sees two Filled entries at one price and cannot tell one trade between two orders from
// two separate trades, since nothing else distinguishes them.
public record MarketByOrderDelta(Side Side, string ExchangeOrderId, decimal Price, int Quantity,
    MarketByOrderDeltaAction Action, string? TradeId = null);

// Every order-level change one action made - CME's Market by Order, Eurex's EOBI. A consumer
// replays these onto its own mirrored book.
//
// One message per action carrying all of them, for the reasons MarketByPriceDeltaEvent is: a
// consumer applies the whole message or none of it, so it never reads a book with a swept order
// gone and the aggressor's remainder not yet added; and the channel stamps one sequence per
// message, so every sequence marks a coherent book - which is what lets a snapshot say it is
// consistent as of one.
//
// Unlike the by-price feed this is not capped at ten deep. The two are different products: a
// depth feed publishes a window because that is what fits on the wire, where an order-by-order
// feed exists precisely to carry the whole book.
public record MarketByOrderDeltaEvent(string Symbol, DateTime Time, IReadOnlyList<MarketByOrderDelta> Changes)
    : MarketDataEvent(Symbol, Time)
{
    // Spelled out for the reason the by-price message spells them out: a record's generated
    // equality compares a list member by reference, and its ToString renders one as a type name.
    public virtual bool Equals(MarketByOrderDeltaEvent? other) =>
        other is not null
        && EqualityContract == other.EqualityContract
        && Symbol == other.Symbol
        && Time == other.Time
        && Changes.SequenceEqual(other.Changes);

    public override int GetHashCode() => HashCode.Combine(Symbol, Time, Changes.Count);

    public override string ToString() =>
        $"{nameof(MarketByOrderDeltaEvent)} {{ Symbol = {Symbol}, Time = {Time:O}, " +
        $"Changes = [{string.Join(", ", Changes)}] }}";
}
