using System;
using System.Collections.Generic;

namespace Circus.OrderBook
{
    public record OrderBookEvent(Security Security, DateTime Time);

    public record StatusChanged(Security Security, DateTime Time, OrderBookStatus Status)
        : OrderBookEvent(Security, Time);

    public record OrderEvent(Security Security, DateTime Time, string CompanyId, string ClientOrderId,
            string? ExchangeOrderId)
        : OrderBookEvent(Security, Time);

    public record OrderConfirmedEvent(Security Security, DateTime Time, string CompanyId, Order Order)
        : OrderEvent(Security, Time, CompanyId, Order.ClientOrderId, Order.ExchangeOrderId);

    public record CreateOrderConfirmed(Security Security, DateTime Time, string CompanyId, Order Order)
        : OrderConfirmedEvent(Security, Time, CompanyId, Order);

    // PreviousPrice/PreviousQuantity describe the order's working-book state immediately before
    // this update - needed because Order reflects the post-update state, and price/quantity may
    // both have changed. PreviousPrice is null when the order wasn't previously resting in the
    // working book at all (a stop order activating into a working limit order), distinguishing
    // an arrival from a move between two working-book levels. PreviousQuantity is the order's
    // DisplayedQuantity (not RemainingQuantity) immediately before the update, so it lines up
    // with what a working-book level actually contained - for an iceberg, the hidden reserve was
    // never part of any level's displayed size.
    public record UpdateOrderConfirmed(Security Security, DateTime Time, string CompanyId, Order Order,
            string PreviousClientOrderId, decimal? PreviousPrice, int PreviousQuantity)
        : OrderConfirmedEvent(Security, Time, CompanyId, Order);

    // PreviousQuantity is the order's DisplayedQuantity immediately before cancellation (not
    // RemainingQuantity - an iceberg's hidden reserve was never part of the working-book level
    // this order is being removed from). Captured explicitly rather than read off Order.
    // DisplayedQuantity post-cancel, even though Cancel() happens to leave DisplayedQuantity
    // untouched today - relying on that would be fragile. PreviousPrice is null when the
    // cancelled order was still Hidden (a stop order cancelled before it triggered) - it was
    // never resting in the working book, so there's no working-book level to remove it from.
    public record CancelOrderConfirmed(Security Security, DateTime Time, string CompanyId, Order Order,
            string PreviousClientOrderId, OrderCancelledReason Reason, decimal? PreviousPrice, int PreviousQuantity)
        : OrderConfirmedEvent(Security, Time, CompanyId, Order);

    // PreviousQuantity is the order's DisplayedQuantity immediately before expiry - see
    // CancelOrderConfirmed. PreviousPrice is null when the expired order was still Hidden (a
    // stop order expiring before it triggered).
    public record ExpireOrderConfirmed(Security Security, DateTime Time, string CompanyId, Order Order,
            decimal? PreviousPrice, int PreviousQuantity)
        : OrderConfirmedEvent(Security, Time, CompanyId, Order);

    public record FillOrderConfirmed(Security Security, DateTime Time, string CompanyId, Order Order, decimal Price,
            int Quantity, bool IsResting)
        : OrderConfirmedEvent(Security, Time, CompanyId, Order);

    public record OrderRejectedEvent(Security Security, DateTime Time, string CompanyId, string ClientOrderId,
            string? ExchangeOrderId, OrderRejectedReason Reason)
        : OrderEvent(Security, Time, CompanyId, ClientOrderId, ExchangeOrderId);

    // Create is always rejected before an order (and thus an ExchangeOrderId) exists.
    public record CreateOrderRejected(Security Security, DateTime Time, string CompanyId, string ClientOrderId,
            OrderRejectedReason Reason)
        : OrderRejectedEvent(Security, Time, CompanyId, ClientOrderId, null, Reason);

    // ExchangeOrderId is populated once the target order has been located (null for rejections
    // that occur before lookup, e.g. MarketClosed or an invalid ClientOrderId).
    public record UpdateOrderRejected(Security Security, DateTime Time, string CompanyId, string ClientOrderId,
            string PreviousClientOrderId, string? ExchangeOrderId, OrderRejectedReason Reason)
        : OrderRejectedEvent(Security, Time, CompanyId, ClientOrderId, ExchangeOrderId, Reason);

    public record CancelOrderRejected(Security Security, DateTime Time, string CompanyId, string ClientOrderId,
            string PreviousClientOrderId, string? ExchangeOrderId, OrderRejectedReason Reason)
        : OrderRejectedEvent(Security, Time, CompanyId, ClientOrderId, ExchangeOrderId, Reason);

    public record OrdersMatched(Security Security, DateTime Time, decimal Price, int Quantity,
            IList<FillOrderConfirmed> Fills)
        : OrderBookEvent(Security, Time);
}