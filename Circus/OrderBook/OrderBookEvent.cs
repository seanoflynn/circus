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

    // Previous* describe the working-book state before this update, since Order reflects the state
    // after it. PreviousPrice is null when the order was not previously in the working book at all
    // (a stop activating), distinguishing an arrival from a move between levels. PreviousQuantity is
    // DisplayedQuantity, matching what a level actually contained.
    //
    // PreviousExchangeOrderId differs from Order.ExchangeOrderId whenever the update lost time
    // priority, and is equal for a quantity decrease, which keeps it. A full-book feed uses this to
    // tell a requeue apart from an in-place modify.
    public record UpdateOrderConfirmed(Security Security, DateTime Time, string CompanyId, Order Order,
            string PreviousClientOrderId, string PreviousExchangeOrderId, decimal? PreviousPrice, int PreviousQuantity)
        : OrderConfirmedEvent(Security, Time, CompanyId, Order);

    // PreviousQuantity is DisplayedQuantity before cancellation - an iceberg's hidden reserve was
    // never part of the level being removed from. PreviousPrice is null when the order was still
    // Hidden, having never rested in the working book.
    public record CancelOrderConfirmed(Security Security, DateTime Time, string CompanyId, Order Order,
            string PreviousClientOrderId, OrderCancelledReason Reason, decimal? PreviousPrice, int PreviousQuantity)
        : OrderConfirmedEvent(Security, Time, CompanyId, Order);

    // As CancelOrderConfirmed: DisplayedQuantity before expiry, and a null price for an order that
    // was still Hidden.
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