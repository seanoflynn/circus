using System;
using System.Collections.Generic;

namespace Circus.OrderBook
{
    public record OrderBookEvent(Security Security, DateTime Time);

    public record StatusChanged(Security Security, DateTime Time, OrderBookStatus Status)
        : OrderBookEvent(Security, Time);

    public record OrderEvent(Security Security, DateTime Time, Guid ClientId, Guid OrderId)
        : OrderBookEvent(Security, Time);

    public record CreateOrderConfirmed(Security Security, DateTime Time, Guid ClientId, Order Order)
        : OrderEvent(Security, Time, ClientId, Order.OrderId);

    public record UpdateOrderConfirmed(Security Security, DateTime Time, Guid ClientId, Order Order)
        : OrderEvent(Security, Time, ClientId, Order.OrderId);

    public record CancelOrderConfirmed(Security Security, DateTime Time, Guid ClientId, Order Order,
            OrderCancelledReason Reason)
        : OrderEvent(Security, Time, ClientId, Order.OrderId);

    public record ExpireOrderConfirmed(Security Security, DateTime Time, Guid ClientId, Order Order)
        : OrderEvent(Security, Time, ClientId, Order.OrderId);

    public record CreateOrderRejected(Security Security, DateTime Time, Guid ClientId, Guid OrderId,
            OrderRejectedReason Reason)
        : OrderEvent(Security, Time, ClientId, OrderId);

    public record UpdateOrderRejected(Security Security, DateTime Time, Guid ClientId, Guid OrderId,
            OrderRejectedReason Reason)
        : OrderEvent(Security, Time, ClientId, OrderId);

    public record CancelOrderRejected(Security Security, DateTime Time, Guid ClientId, Guid OrderId,
            OrderRejectedReason Reason)
        : OrderEvent(Security, Time, ClientId, OrderId);

    public record OrderFilled(Security Security, DateTime Time, Guid ClientId, Guid OrderId, Order Order, decimal Price,
            int Quantity, bool IsResting)
        : OrderEvent(Security, Time, ClientId, OrderId);

    public record OrdersMatched(Security Security, DateTime Time, decimal Price, int Quantity,
            IList<OrderFilled> Fills)
        : OrderBookEvent(Security, Time);
}