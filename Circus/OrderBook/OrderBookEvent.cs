using System;
using System.Collections.Generic;

namespace Circus.OrderBook
{
    public class OrderBookEventArgs : EventArgs
    {
        public OrderBookEventArgs(IList<OrderBookEvent> events)
        {
            Events = events;
        }

        public IList<OrderBookEvent> Events { get; }
    }

    public record OrderBookEvent(Security Security, DateTime Time);

    public record StatusChanged(Security Security, DateTime Time, OrderBookStatus Status)
        : OrderBookEvent(Security, Time);

    public record OrderEvent(Security Security, DateTime Time, Guid ClientId)
        : OrderBookEvent(Security, Time);

    public record OrderRejectedEvent(Security Security, DateTime Time, Guid ClientId, Guid OrderId)
        : OrderEvent(Security, Time, ClientId);

    public record OrderConfirmedEvent(Security Security, DateTime Time, Guid ClientId, Order Order)
        : OrderEvent(Security, Time, ClientId);

    public record CreateOrderConfirmed(Security Security, DateTime Time, Guid ClientId, Order Order)
        : OrderConfirmedEvent(Security, Time, ClientId, Order);

    public record CreateOrderRejected(Security Security, DateTime Time, Guid ClientId, Guid OrderId,
            OrderRejectedReason Reason)
        : OrderRejectedEvent(Security, Time, ClientId, OrderId);

    public record UpdateOrderConfirmed(Security Security, DateTime Time, Guid ClientId, Order Order)
        : OrderConfirmedEvent(Security, Time, ClientId, Order);

    public record UpdateOrderRejected(Security Security, DateTime Time, Guid ClientId, Guid OrderId,
            OrderRejectedReason Reason)
        : OrderRejectedEvent(Security, Time, ClientId, OrderId);

    public record CancelOrderConfirmed(Security Security, DateTime Time, Guid ClientId, Order Order,
            OrderCancelledReason Reason)
        : OrderConfirmedEvent(Security, Time, ClientId, Order);

    public record CancelOrderRejected(Security Security, DateTime Time, Guid ClientId, Guid OrderId,
            OrderRejectedReason Reason)
        : OrderRejectedEvent(Security, Time, ClientId, OrderId);

    public record ExpireOrderConfirmed(Security Security, DateTime Time, Guid ClientId, Order Order)
        : OrderConfirmedEvent(Security, Time, ClientId, Order);

    public record OrderMatched(Security Security, DateTime Time, decimal Price, int Quantity, Order Resting,
            Order Aggressor)
        : OrderBookEvent(Security, Time);
}