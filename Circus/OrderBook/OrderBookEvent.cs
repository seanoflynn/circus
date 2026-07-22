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

    public record UpdateOrderConfirmed(Security Security, DateTime Time, string CompanyId, Order Order,
            string PreviousClientOrderId)
        : OrderConfirmedEvent(Security, Time, CompanyId, Order);

    public record CancelOrderConfirmed(Security Security, DateTime Time, string CompanyId, Order Order,
            string PreviousClientOrderId, OrderCancelledReason Reason)
        : OrderConfirmedEvent(Security, Time, CompanyId, Order);

    public record ExpireOrderConfirmed(Security Security, DateTime Time, string CompanyId, Order Order)
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