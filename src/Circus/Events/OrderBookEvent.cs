namespace Circus.Events;

public record OrderBookEvent(string Symbol, DateTime Time);

// Reason defaults to Requested, which is what every externally driven transition is.
//
// ResumesAt is when a timed interruption is due to end, and is null for everything else - which
// includes an interruption configured to last until told otherwise, and every ordinary
// transition, since an explicit one supersedes whatever was pending.
public record StatusChanged(string Symbol, DateTime Time, OrderBookStatus Status,
        OrderBookStatusChangeReason Reason = OrderBookStatusChangeReason.Requested, DateTime? ResumesAt = null)
    : OrderBookEvent(Symbol, Time);

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
// broadcasts is derived from these instead - see TradeDataProducer - which keeps the private and
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
    : OrderBookEvent(Symbol, Time);

// The market has reached a daily price limit and cannot trade through it, or has come back
// inside one. Side is which way it is stuck: Buy for limit up, where buyers cannot push higher,
// Sell for limit down. Null with a null Price releases it, and is what a print inside the
// limits emits.
//
// Not a status change - a limit-locked market is open, quoting, and trading at the limit. That
// is the whole difference between a limit and a circuit breaker, so it gets an event of its own
// rather than a status that would claim otherwise. Emitted only on a change.
public record LimitStateChanged(string Symbol, DateTime Time, Side? Side, decimal? Price)
    : OrderBookEvent(Symbol, Time);