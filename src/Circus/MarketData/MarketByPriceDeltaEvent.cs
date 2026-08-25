using Circus.Events;

namespace Circus.MarketData;

public record MarketByPriceDelta(Side Side, int LevelIndex, decimal Price, int Quantity, int Count,
    LevelChangeAction Action);

public record MarketByPriceDeltaEvent(string Symbol, DateTime Time, int Depth,
        IReadOnlyList<MarketByPriceDelta> Changes)
    : MarketDataEvent(Symbol, Time)
{
    // Spelled out because a record's generated equality compares a list member by reference, and
    // its ToString renders one as a type name.
    public virtual bool Equals(MarketByPriceDeltaEvent? other) =>
        other is not null
        && EqualityContract == other.EqualityContract
        && Symbol == other.Symbol
        && Time == other.Time
        && Depth == other.Depth
        && Changes.SequenceEqual(other.Changes);

    public override int GetHashCode() => HashCode.Combine(Symbol, Time, Depth, Changes.Count);

    public override string ToString() =>
        $"{nameof(MarketByPriceDeltaEvent)} {{ Symbol = {Symbol}, Time = {Time:O}, " +
        $"Depth = {Depth}, Changes = [{string.Join(", ", Changes)}] }}";
}
