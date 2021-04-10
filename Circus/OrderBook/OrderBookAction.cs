using System;

namespace Circus.OrderBook
{
    public record OrderBookAction(Security Security);

    public record UpdateStatus(Security Security, OrderBookStatus Status)
        : OrderBookAction(Security);

    public record OrderAction(Security Security, Guid ClientId, Guid OrderId)
        : OrderBookAction(Security);

    public record CreateOrder(Security Security, Guid ClientId, Guid OrderId, OrderValidity OrderValidity, Side Side,
            int Quantity, decimal? Price = null, decimal? TriggerPrice = null)
        : OrderAction(Security, ClientId, OrderId);

    public record UpdateOrder(Security Security, Guid ClientId, Guid OrderId, int? Quantity = null,
            decimal? Price = null, decimal? TriggerPrice = null)
        : OrderAction(Security, ClientId, OrderId);

    public record CancelOrder(Security Security, Guid ClientId, Guid OrderId)
        : OrderAction(Security, ClientId, OrderId);
}