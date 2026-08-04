namespace Circus.MarketData;

// One change to one aggregated price level, as carried inside a by-price message.
//
// Quantity is displayed size, never remaining: an iceberg's hidden reserve is not on the book.
// Quantity and Count are both zero on Removed.
//
// Keyed on Price rather than on LevelIndex, so each change is idempotent and can be applied on
// its own. CME's MDPriceLevel is positional - a New at level 2 shifts everything below it down -
// which means a consumer that mislays one has every level beneath it wrong. LevelIndex is the
// rank the level holds, or on Removed the rank it last held, and is carried for a consumer that
// wants it rather than for identifying the level.
public record MarketByPriceDelta(Side Side, int LevelIndex, decimal Price, int Quantity, int Count,
    MarketByPriceDeltaAction Action);

// Every price level one action moved, in one message - CME's Market by Price, Eurex's EMDI. The
// incremental half of the by-price feed; the periodic full image is the snapshot half.
//
// One message rather than one per level, which is what MDIncrementalRefreshBook is: an aggressor
// sweeping three levels is a single book update carrying three entries, not three updates. Two
// things follow from that, and they are the reason for it. A subscriber applies the whole message
// or none of it, so it never reads a book with the swept levels gone and the aggressor's
// remainder not yet added. And the channel stamps one sequence number per message, so every
// sequence marks a coherent book - which is what lets a snapshot say it is consistent as of one.
//
// The book reports the same set as one LevelsChanged, and this is its published form. The two
// stay separate types because what a book says about itself and what a subscriber is told are
// different vocabularies - the same reason FillOrderConfirmed and TradeDataEvent are not one
// type - not because either needs reassembling on the way through.
//
// Changes are ordered best price outward within a side, arrivals and changes before departures.
public record MarketByPriceDeltaEvent(string Symbol, DateTime Time, IReadOnlyList<MarketByPriceDelta> Changes)
    : MarketDataEvent(Symbol, Time)
{
    // Both spelled out for the reason LevelsChanged spells them out: a record's generated equality
    // compares a list member by reference, and its ToString renders one as a type name. This is
    // the only published message carrying a collection, and it should compare and print like every
    // other one rather than being the exception nobody remembers.
    public virtual bool Equals(MarketByPriceDeltaEvent? other) =>
        other is not null
        && EqualityContract == other.EqualityContract
        && Symbol == other.Symbol
        && Time == other.Time
        && Changes.SequenceEqual(other.Changes);

    public override int GetHashCode() => HashCode.Combine(Symbol, Time, Changes.Count);

    public override string ToString() =>
        $"{nameof(MarketByPriceDeltaEvent)} {{ Symbol = {Symbol}, Time = {Time:O}, " +
        $"Changes = [{string.Join(", ", Changes)}] }}";
}
