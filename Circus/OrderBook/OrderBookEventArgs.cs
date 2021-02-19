using System;
using System.Collections.Generic;

namespace Circus.OrderBook
{
    public record OrderCreatedSuccessEventArgs(Order Order);

    public record OrderCreateRejectedEventArgs(Guid OrderId, OrderRejectedReason Reason);

    public record OrderUpdatedSuccessEventArgs(Order Order);

    public record OrderUpdateRejectedEventArgs(Guid OrderId, OrderRejectedReason Reason);

    public record OrderCancelledSuccessEventArgs(Order Order, OrderCancelledReason Reason);

    public record OrderCancelRejectedEventArgs(Guid OrderId, OrderRejectedReason Reason);

    public record OrderExpiredEventArgs(Order Order);

    public record OrderFilledEventArgs(Fill Fill);

    public record TradedEventArgs(List<Fill> Fills);
}