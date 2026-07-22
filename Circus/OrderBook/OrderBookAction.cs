using System;

namespace Circus.OrderBook
{
    public record OrderBookAction(Security Security);

    public record UpdateStatus(Security Security, OrderBookStatus Status)
        : OrderBookAction(Security);

    public record OrderAction(Security Security, string CompanyId, string ClientOrderId)
        : OrderBookAction(Security);

    public record CreateOrder(Security Security, string CompanyId, string ClientOrderId, OrderValidity OrderValidity,
            Side Side, int Quantity, decimal? Price = null, decimal? TriggerPrice = null, bool MarketLimit = false,
            DateOnly? GoodTilDate = null)
        : OrderAction(Security, CompanyId, ClientOrderId);

    public record UpdateOrder(Security Security, string CompanyId, string ClientOrderId, string PreviousClientOrderId,
            int? Quantity = null, decimal? Price = null, decimal? TriggerPrice = null)
        : OrderAction(Security, CompanyId, ClientOrderId);

    public record CancelOrder(Security Security, string CompanyId, string ClientOrderId, string PreviousClientOrderId)
        : OrderAction(Security, CompanyId, ClientOrderId);
}