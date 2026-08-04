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
// The book reports these singly, as LevelChanged, and this composes them. Same split as a trade:
// FillOrderConfirmed is emitted per side because a fill belongs to one participant, and the
// public print is derived from the pair. Flat events compare by value, which is what keeps a
// replay assertable event by event; the composing belongs on the way out.
//
// Changes are ordered best price outward within a side, arrivals and changes before departures.
public record MarketByPriceDeltaEvent(string Symbol, DateTime Time, IReadOnlyList<MarketByPriceDelta> Changes)
    : MarketDataEvent(Symbol, Time)
{
    // A record's generated ToString renders a list as its type name, and this is the only
    // published message carrying one. Spelled out so a printed feed is readable and so the tests
    // that compare two runs by rendering them still see the contents.
    public override string ToString() =>
        $"{nameof(MarketByPriceDeltaEvent)} {{ Symbol = {Symbol}, Time = {Time:O}, " +
        $"Changes = [{string.Join(", ", Changes)}] }}";
}
