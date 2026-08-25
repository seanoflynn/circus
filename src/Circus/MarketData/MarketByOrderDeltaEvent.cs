using Circus.Events;

namespace Circus.MarketData;

public record MarketByOrderDelta(Side Side, string ExchangeOrderId, decimal Price, int Quantity,
    OrderChangeAction Action, string? TradeId = null);

public record MarketByOrderDeltaEvent(string Symbol, DateTime Time, IReadOnlyList<MarketByOrderDelta> Changes)
    : MarketDataEvent(Symbol, Time)
{
    // Spelled out for the reason MarketByPriceDeltaEvent spells them out: generated record
    // equality would compare Changes by reference.
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
