using System;

namespace Circus.OrderBook
{
    public record OrderBookAction(Security Security);

    public record UpdateStatus(Security Security, OrderBookStatus Status)
        : OrderBookAction(Security);

    public record OrderAction(Security Security, Guid ClientId, Guid OrderId)
        : OrderBookAction(Security);

    public record CreateLimitOrder(Security Security, Guid ClientId, Guid OrderId, OrderValidity OrderValidity,
            Side Side, decimal Price, int Quantity)
        : OrderAction(Security, ClientId, OrderId);

    public record CreateMarketOrder(Security Security, Guid ClientId, Guid OrderId, OrderValidity OrderValidity,
            Side Side, int Quantity)
        : OrderAction(Security, ClientId, OrderId);

    public record UpdateLimitOrder(Security Security, Guid ClientId, Guid OrderId, decimal Price, int Quantity)
        : OrderAction(Security, ClientId, OrderId);

    public record CancelOrder(Security Security, Guid ClientId, Guid OrderId)
        : OrderAction(Security, ClientId, OrderId);
}