using System;
using System.Collections.Generic;
using Circus.Enums;

namespace Circus.OrderBook
{
    public record OrderCreatedSuccessEventArgs(Order Order);

    public record OrderCreateRejectedEventArgs(Guid OrderId, RejectReason Reason);

    public record OrderUpdatedSuccessEventArgs(Order Order);

    public record OrderUpdateRejectedEventArgs(Guid OrderId, RejectReason Reason);

    public record OrderDeletedSuccessEventArgs(Order Order, OrderDeletedReason Reason);

    public record OrderDeleteRejectedEventArgs(Guid OrderId, RejectReason Reason);

    public record OrderExpiredEventArgs(Order Order);

    public record OrderFilledEventArgs(Fill Fill);

    public record TradedEventArgs(List<Fill> Fills);

    public record BookUpdatedEventArgs(DateTime Time, Security Security);
}