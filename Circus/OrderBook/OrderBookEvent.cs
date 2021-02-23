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

    public record OrderBookEvent(DateTime Time);

    public record OrderBookStateChangedEvent(DateTime Time, OrderBookStatus Status) : OrderBookEvent(Time);

    public record OrderCreatedEvent(DateTime Time, Order Order) : OrderBookEvent(Time);

    public record OrderCreateRejectedEvent(DateTime Time, Guid OrderId, OrderRejectedReason Reason) 
        : OrderBookEvent(Time);

    public record OrderUpdatedEvent(DateTime Time, Order Order) : OrderBookEvent(Time);

    public record OrderUpdateRejectedEvent(DateTime Time, Guid OrderId, OrderRejectedReason Reason) 
        : OrderBookEvent(Time);

    public record OrderCancelledEvent(DateTime Time, Order Order, OrderCancelledReason Reason) : OrderBookEvent(Time);

    public record OrderCancelRejectedEvent(DateTime Time, Guid OrderId, OrderRejectedReason Reason) 
        : OrderBookEvent(Time);

    public record OrderExpiredEvent(DateTime Time, Order Order) : OrderBookEvent(Time);

    public record OrderMatchedEvent(DateTime Time, decimal Price, int Quantity, Order Resting, Order Aggressor) 
        : OrderBookEvent(Time);
}