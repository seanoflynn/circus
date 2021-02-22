using System;
using System.Collections.Generic;

namespace Circus.OrderBook
{
    public record OrderBookEvent;
    
    public record OrderCreatedEvent(Order Order) : OrderBookEvent;

    public record OrderCreateRejectedEvent(Guid OrderId, OrderRejectedReason Reason) : OrderBookEvent;

    public record OrderUpdatedEvent(Order Order) : OrderBookEvent;

    public record OrderUpdateRejectedEvent(Guid OrderId, OrderRejectedReason Reason) : OrderBookEvent;

    public record OrderCancelledEvent(Order Order, OrderCancelledReason Reason) : OrderBookEvent;

    public record OrderCancelRejectedEvent(Guid OrderId, OrderRejectedReason Reason) : OrderBookEvent;

    public record OrderExpiredEvent(Order Order) : OrderBookEvent;

    public record OrderMatchedEvent(Fill Fill, Order Resting, Order Aggressor) : OrderBookEvent;

    // public record TradedEventArgs(List<Fill> Fills);
}