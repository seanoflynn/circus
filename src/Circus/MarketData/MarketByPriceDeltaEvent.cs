using Circus.Events;

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
// Action is the book's own LevelChangeAction rather than a published enum of its own. The message
// stays a separate type - see below - but the three things that can happen to a level do not
// differ between saying it and hearing it, and a private enum whose members were copied across one
// for one bought nothing but a switch to write them out in. If the two ever do need to differ,
// this is where a mapping goes back.
public record MarketByPriceDelta(Side Side, int LevelIndex, decimal Price, int Quantity, int Count,
    LevelChangeAction Action);

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
// type - not because either needs reassembling on the way through. What that buys is room for
// the two to differ, and they already do: Depth is a fact about the feed carrying the message and
// means nothing to the book, which reports at every depth anyone asked for.
//
// Changes are ordered best price outward within a side, arrivals and changes before departures.
//
// Depth is how many levels a side of this feed's window holds - CME's ten for futures, a
// top-of-book product's one - and a subscriber needs it to know what a departure means: at ten
// deep a Removed says the level emptied, at one deep it usually says a better price arrived.
// A venue publishing the same book at two depths publishes two streams of these, because a
// shallower one is not a filtered deeper one; see LevelsChanged for why.
public record MarketByPriceDeltaEvent(string Symbol, DateTime Time, int Depth,
        IReadOnlyList<MarketByPriceDelta> Changes)
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
        && Depth == other.Depth
        && Changes.SequenceEqual(other.Changes);

    public override int GetHashCode() => HashCode.Combine(Symbol, Time, Depth, Changes.Count);

    public override string ToString() =>
        $"{nameof(MarketByPriceDeltaEvent)} {{ Symbol = {Symbol}, Time = {Time:O}, " +
        $"Depth = {Depth}, Changes = [{string.Join(", ", Changes)}] }}";
}
