using Circus.MarketData;

namespace Circus.Events;

public record OrderBookEvent(string Symbol, DateTime Time);

// Everything a venue may broadcast, and the half of the book's output that carries no client
// identity. A market data feed is handed these and only these, so a CompanyId reaching a
// public feed is a compile error rather than something a reflection test has to go looking for.
//
// The other half is OrderEvent below: what happened to one participant's order, addressed to that
// participant. Real venues keep the two apart at the protocol level - CME answers order entry on
// iLink and publishes market data on MDP, Eurex uses ETI and EMDI/EOBI - and the distinction here
// is the same one, drawn in the type system rather than across a wire.
//
// Both travel in one stream out of Process. That is deliberate: their interleaving is meaningful,
// since when a participant learns of its own fill relative to when the market learns of the print
// is exactly the sort of thing a venue simulator exists to answer.
public abstract record MarketEvent(string Symbol, DateTime Time) : OrderBookEvent(Symbol, Time);

// Reason defaults to Requested, which is what every externally driven transition is.
//
// ResumesAt is when a timed interruption is due to end, and is null for everything else - which
// includes an interruption configured to last until told otherwise, and every ordinary
// transition, since an explicit one supersedes whatever was pending.
//
// LimitState is which way a daily limit has the market stuck as this transition happens, and is
// not a claim that the limit moved - LimitStateChanged below says that. It is carried for the
// reason UpdateOrderConfirmed carries its Previous fields: so that a consumer wanting the
// instrument's whole state does not have to remember the half this event is not about. The book
// already holds it, and BookSnapshot already publishes the same composite, so restating it here
// costs a field and saves every subscriber an accumulator that can drift and cannot resync.
public record StatusChanged(string Symbol, DateTime Time, OrderBookStatus Status,
        OrderBookStatusChangeReason Reason = OrderBookStatusChangeReason.Requested, DateTime? ResumesAt = null,
        Side? LimitState = null)
    : MarketEvent(Symbol, Time);

// Addressed to one participant, and never broadcast: CompanyId and ClientOrderId identify who
// sent the order, which is the participant's business and nobody else's. Everything private the
// book emits descends from this, so "not a MarketEvent" and "carries client identity" are the
// same statement.
public record OrderEvent(string Symbol, DateTime Time, string CompanyId, string ClientOrderId,
        string? ExchangeOrderId)
    : OrderBookEvent(Symbol, Time);

public record OrderConfirmedEvent(string Symbol, DateTime Time, string CompanyId, Order Order)
    : OrderEvent(Symbol, Time, CompanyId, Order.ClientOrderId, Order.ExchangeOrderId);

public record CreateOrderConfirmed(string Symbol, DateTime Time, string CompanyId, Order Order)
    : OrderConfirmedEvent(Symbol, Time, CompanyId, Order);

// Previous* describe the working-book state before this update, since Order reflects the state
// after it. PreviousPrice is null when the order was not previously in the working book at all
// (a stop activating), distinguishing an arrival from a move between levels. PreviousQuantity is
// DisplayedQuantity, matching what a level actually contained.
//
// PreviousExchangeOrderId differs from Order.ExchangeOrderId whenever the update lost time
// priority, and is equal for a quantity decrease, which keeps it. A full-book feed uses this to
// tell a requeue apart from an in-place modify.
public record UpdateOrderConfirmed(string Symbol, DateTime Time, string CompanyId, Order Order,
        string PreviousClientOrderId, string PreviousExchangeOrderId, decimal? PreviousPrice, int PreviousQuantity)
    : OrderConfirmedEvent(Symbol, Time, CompanyId, Order);

// PreviousQuantity is DisplayedQuantity before cancellation - an iceberg's hidden reserve was
// never part of the level being removed from. PreviousPrice is null when the order was still
// Hidden, having never rested in the working book.
public record CancelOrderConfirmed(string Symbol, DateTime Time, string CompanyId, Order Order,
        string PreviousClientOrderId, OrderCancelledReason Reason, decimal? PreviousPrice, int PreviousQuantity)
    : OrderConfirmedEvent(Symbol, Time, CompanyId, Order);

// As CancelOrderConfirmed: DisplayedQuantity before expiry, and a null price for an order that
// was still Hidden.
public record ExpireOrderConfirmed(string Symbol, DateTime Time, string CompanyId, Order Order,
        decimal? PreviousPrice, int PreviousQuantity)
    : OrderConfirmedEvent(Symbol, Time, CompanyId, Order);

// One side of one trade, and the whole of what the book says about it. A trade is one resting
// order matched against one aggressor, so it produces exactly two of these - the resting side
// first - sharing a TradeId and differing in IsResting.
//
// Not wrapped in an event carrying both sides, which is what OrdersMatched used to be. A fill
// belongs to one participant and carries their CompanyId, so a feed for one of them is a filter
// over these; a wrapper holding both sides could only be filtered by rewriting it, and handing
// one participant the other's Order was a leak waiting to be shipped. The public print a venue
// broadcasts is derived from these instead - see InstrumentFeed - which keeps the private and
// public views of a trade apart the way a real venue's execution reports and trade feed are.
//
// TradeId identifies the trade within this instrument, not beyond it, exactly as
// ExchangeOrderId identifies an order within it: the venue-wide identity is the pair
// (Instrument, TradeId).
//
// Quantity is what traded; PreviousDisplayedQuantity is the order's DisplayedQuantity before
// it did. The two differ whenever an auction sizes a fill off full remaining quantity - an
// iceberg can trade more than it was showing, and comes out of it displaying a fresh peak
// rather than what is left of the old one. A level aggregate must move by the change in
// displayed size, not by the traded quantity.
public record FillOrderConfirmed(string Symbol, DateTime Time, string CompanyId, Order Order, string TradeId,
        decimal Price, int Quantity, int PreviousDisplayedQuantity, bool IsResting)
    : OrderConfirmedEvent(Symbol, Time, CompanyId, Order);

public record OrderRejectedEvent(string Symbol, DateTime Time, string CompanyId, string ClientOrderId,
        string? ExchangeOrderId, OrderRejectedReason Reason)
    : OrderEvent(Symbol, Time, CompanyId, ClientOrderId, ExchangeOrderId);

// Create is always rejected before an order (and thus an ExchangeOrderId) exists.
public record CreateOrderRejected(string Symbol, DateTime Time, string CompanyId, string ClientOrderId,
        OrderRejectedReason Reason)
    : OrderRejectedEvent(Symbol, Time, CompanyId, ClientOrderId, null, Reason);

// ExchangeOrderId is populated once the target order has been located (null for rejections
// that occur before lookup, e.g. MarketClosed or an invalid ClientOrderId).
public record UpdateOrderRejected(string Symbol, DateTime Time, string CompanyId, string ClientOrderId,
        string PreviousClientOrderId, string? ExchangeOrderId, OrderRejectedReason Reason)
    : OrderRejectedEvent(Symbol, Time, CompanyId, ClientOrderId, ExchangeOrderId, Reason);

public record CancelOrderRejected(string Symbol, DateTime Time, string CompanyId, string ClientOrderId,
        string PreviousClientOrderId, string? ExchangeOrderId, OrderRejectedReason Reason)
    : OrderRejectedEvent(Symbol, Time, CompanyId, ClientOrderId, ExchangeOrderId, Reason);

// The price and quantity the current phase would print if it ended right now - an auction's
// indicative quote, published as it moves rather than answered on request, so a consumer's
// view of it follows from the event stream alone. Emitted only on a change, which makes a
// null Price (with Quantity 0) the withdrawal of a quote previously published: the book has
// stopped crossing, or the phase quoting it has ended. A phase that trades continuously has
// no such price and so publishes none.
public record IndicativePriceChanged(string Symbol, DateTime Time, decimal? Price, int Quantity)
    : MarketEvent(Symbol, Time);

// The market has reached a daily price limit and cannot trade through it, or has come back
// inside one. Side is which way it is stuck: Buy for limit up, where buyers cannot push higher,
// Sell for limit down. Null with a null Price releases it, and is what a print inside the
// limits emits.
//
// Not a status change - a limit-locked market is open, quoting, and trading at the limit. That
// is the whole difference between a limit and a circuit breaker, so it gets an event of its own
// rather than a status that would claim otherwise. Emitted only on a change.
//
// Status, Reason and ResumesAt are what the instrument's status is while this happens, and say
// nothing about it having moved - it did not, which is the paragraph above. They are here for the
// same reason StatusChanged carries LimitState: either event is then a complete picture of the
// instrument's state, so a consumer assembling that picture holds nothing between messages and a
// gap costs it the update rather than the truth. That the two stay separate types is what keeps
// "the status moved" and "the limit moved" tellable apart by a consumer that cares about one and
// not the other - which is the distinction the paragraph above exists to protect, and merging
// them into one event would have given up.
public record LimitStateChanged(string Symbol, DateTime Time, Side? Side, decimal? Price,
        OrderBookStatus Status, OrderBookStatusChangeReason Reason, DateTime? ResumesAt)
    : MarketEvent(Symbol, Time);
// One aggregated price level of the working book, as carried inside a LevelsChanged.
//
// Quantity is displayed size, never remaining: an iceberg's hidden reserve is not on the book.
// Both are zero on Removed.
//
// Keyed on Price, not on LevelIndex. CME's MDPriceLevel is positional - a New at level 2 shifts
// everything below it down - which means a consumer that mislays one has every level beneath it
// wrong. Keying on price makes each change idempotent and independently applicable, and means a
// level whose rank moved because a better one appeared says nothing at all, where a positional
// feed would restate the whole ladder beneath it. LevelIndex is the rank the level holds, or on
// Removed the rank it last held, and is carried for a consumer that wants it rather than for
// identifying the level.
//
// Removed covers a level leaving the published window as well as leaving the book - a level
// pushed past the window's depth is no longer published whether or not orders still rest there.
// It returns as Added if it comes back.
public record LevelChange(Side Side, int LevelIndex, decimal Price, int Quantity, int Count,
    LevelChangeAction Action);

// Every aggregated price level one action moved - the same category as IndicativePriceChanged and
// LimitStateChanged above: not something a client did, but something only the book can see about
// itself, reported so a consumer never has to reconstruct it.
//
// The price ladders carry a running total of displayed size and order count per level, so the
// book publishes what it already knows. A by-price feed is then a translation of these rather
// than a second book kept in step with this one - which is the whole reason they exist, since
// deriving them means holding the book the subscriber is missing.
//
// One event per action carrying every level it moved, rather than one per level. The set of
// levels an action moved is a single fact about that action: an aggressor sweeping three of them
// took the book from one state to another in one step, and splitting that leaves every consumer
// to recover the grouping before it can act on it. Emitted only when something actually moved, so
// an action that touches no level - a status change, a rejected order - reports nothing.
//
// Changes are ordered best price outward within a side, arrivals and changes before departures.
//
// Depth is how many levels a side of the window holds, and it is part of what the event says
// rather than a note about it: the same action produces a different set of changes at five deep
// than at ten, so a report is only meaningful paired with the window it describes. A book asked
// for several depths answers with one of these per depth, and a feed takes the one it publishes.
//
// It has to work that way, because a shallower report is not a filtered deeper one. Bids at
// 200/190/180/170/160/150 with a new bid arriving at 195: ten deep, only the arrival is news, and
// price-keyed reporting deliberately says nothing about the levels that merely moved rank. Five
// deep, 160 has been pushed out of the window and has to be Removed - a change that appears
// nowhere in the ten-deep report, at any rank. Truncating by LevelIndex would silently leave a
// five-deep subscriber holding a level that is no longer published.
public record LevelsChanged(string Symbol, DateTime Time, int Depth,
        IReadOnlyList<LevelChange> Changes)
    : MarketEvent(Symbol, Time)
{
    // Spelled out because a record's generated equality compares a list member by reference, and
    // two runs of the same trace build different list instances. DeterminismTests asserts that a
    // replay reproduces every event exactly, by value - which is why OrdersMatched was flattened
    // rather than left wrapping its fills. That property is worth keeping; carrying a collection
    // is not a reason to give it up, only a reason to say what equality means here.
    public virtual bool Equals(LevelsChanged? other) =>
        other is not null
        && EqualityContract == other.EqualityContract
        && Symbol == other.Symbol
        && Time == other.Time
        && Depth == other.Depth
        && Changes.SequenceEqual(other.Changes);

    public override int GetHashCode() => HashCode.Combine(Symbol, Time, Depth, Changes.Count);

    public override string ToString() =>
        $"{nameof(LevelsChanged)} {{ Symbol = {Symbol}, Time = {Time:O}, Depth = {Depth}, " +
        $"Changes = [{string.Join(", ", Changes)}] }}";
}

// Where the book is right now, rather than what just changed about it - the answer to a
// PublishSnapshot, and the only event here that describes state instead of a transition.
//
// It exists because some of what a subscriber holds cannot be derived from a stream it joined
// late. Aggregated depth is the obvious part; the instrument's status is the subtler one, since a
// status change and a limit lock arrive as separate events and a joiner has heard neither. Both
// are carried here, so a snapshot feed can republish them and a subscriber can start from
// something true. That is what CME's snapshot does, carrying instrument status alongside the book.
//
// Levels are the deepest window the book reports, in displayed size - the same aggregate
// LevelsChanged reports moves to. A feed publishing less takes the top of it, which unlike a
// delta is a plain truncation: an image says where the book is, so the first five entries of a
// ten-deep image are exactly the five-deep image. Orders are the whole working book, best price
// outward and in queue order within a price, since an order-by-order product carries the book
// rather than a window.
public record BookSnapshot(string Symbol, DateTime Time, IReadOnlyList<Level> Bids,
        IReadOnlyList<Level> Offers, IReadOnlyList<RestingOrder> Orders, OrderBookStatus Status,
        OrderBookStatusChangeReason StatusReason, DateTime? ResumesAt, Side? LimitState,
        decimal? IndicativePrice, int IndicativeQuantity)
    : MarketEvent(Symbol, Time)
{
    // Spelled out for the reason LevelsChanged spells them out: a record's generated equality
    // compares a list member by reference, and DeterminismTests asserts a replay reproduces every
    // event by value.
    public virtual bool Equals(BookSnapshot? other) =>
        other is not null
        && EqualityContract == other.EqualityContract
        && Symbol == other.Symbol
        && Time == other.Time
        && Status == other.Status
        && StatusReason == other.StatusReason
        && ResumesAt == other.ResumesAt
        && LimitState == other.LimitState
        && IndicativePrice == other.IndicativePrice
        && IndicativeQuantity == other.IndicativeQuantity
        && Bids.SequenceEqual(other.Bids)
        && Offers.SequenceEqual(other.Offers)
        && Orders.SequenceEqual(other.Orders);

    public override int GetHashCode() =>
        HashCode.Combine(Symbol, Time, Status, Bids.Count, Offers.Count, IndicativePrice);

    public override string ToString() =>
        $"{nameof(BookSnapshot)} {{ Symbol = {Symbol}, Time = {Time:O}, Status = {Status}, " +
        $"Bids = [{string.Join(", ", Bids)}], Offers = [{string.Join(", ", Offers)}] }}";
}

// One change to one displayed order, as carried inside an OrdersChanged.
//
// TradeId is set only on Filled, and pairs the two sides of one trade.
public record OrderChange(Side Side, string ExchangeOrderId, decimal Price, int Quantity,
    OrderChangeAction Action, string? TradeId = null);

// Every change to the displayed order book one action made - the order-by-order counterpart of
// LevelsChanged, and the public view of what the private order confirmations above did.
//
// Not a redaction of those confirmations, which is why it is its own event rather than a filter
// over them. The two do not correspond one to one: a stop order created still hidden produces a
// confirmation and nothing here; an update that lost time priority produces one confirmation and
// two changes here, the old id leaving and a new one arriving at the back of the queue. Working
// out which of those happened needs to know what the book did to its own queue, which is why it
// is the book that says so.
//
// Quantity is displayed size for Added and Modified, what left for Removed, and what traded for
// Filled. An iceberg's hidden reserve is never here.
public record OrdersChanged(string Symbol, DateTime Time, IReadOnlyList<OrderChange> Changes)
    : MarketEvent(Symbol, Time)
{
    public virtual bool Equals(OrdersChanged? other) =>
        other is not null
        && EqualityContract == other.EqualityContract
        && Symbol == other.Symbol
        && Time == other.Time
        && Changes.SequenceEqual(other.Changes);

    public override int GetHashCode() => HashCode.Combine(Symbol, Time, Changes.Count);

    public override string ToString() =>
        $"{nameof(OrdersChanged)} {{ Symbol = {Symbol}, Time = {Time:O}, " +
        $"Changes = [{string.Join(", ", Changes)}] }}";
}

// The public print: one per trade, whatever it took to fill. The private half is the pair of
// FillOrderConfirmed events sharing this TradeId, one per participant.
//
// Emitted by the book rather than derived by a feed from those fills, because a feed only sees
// what a venue broadcasts and a fill is not that - it belongs to the participant whose order
// filled. The pairing the derivation relied on is still here, in the id.
public record TradePrinted(string Symbol, DateTime Time, string TradeId, decimal Price, int Quantity)
    : MarketEvent(Symbol, Time);
