namespace Circus.MarketData;

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
