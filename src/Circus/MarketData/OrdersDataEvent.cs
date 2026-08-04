namespace Circus.MarketData;

// Every order resting in the working book - the by-order snapshot, and the counterpart of
// LevelsDataEvent on the by-price side.
//
// Ordered as the book holds them: best price outward, and within a price by queue position, so a
// consumer rebuilding time priority replays them in the order given and arrives where the book is.
//
// The whole book rather than a window, because that is what an order-by-order product is for. It
// is the heaviest thing this venue publishes, which is why a real one cycles its snapshot feed
// slowly and only a subscriber joining or recovering ever reads it.
public record OrdersDataEvent(string Symbol, DateTime Time, IReadOnlyList<RestingOrder> Orders)
    : MarketDataEvent(Symbol, Time)
{
    public virtual bool Equals(OrdersDataEvent? other) =>
        other is not null
        && EqualityContract == other.EqualityContract
        && Symbol == other.Symbol
        && Time == other.Time
        && Orders.SequenceEqual(other.Orders);

    public override int GetHashCode() => HashCode.Combine(Symbol, Time, Orders.Count);

    public override string ToString() =>
        $"{nameof(OrdersDataEvent)} {{ Symbol = {Symbol}, Time = {Time:O}, " +
        $"Orders = [{string.Join(", ", Orders)}] }}";
}
