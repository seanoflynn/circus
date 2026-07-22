using System;

namespace Circus
{
    public record Order(
        string CompanyId,
        long ExchangeOrderId,
        string ClientOrderId,
        Security Security,
        DateTime CreatedTime,
        DateTime ModifiedTime,
        DateTime? CompletedTime,
        OrderStatus Status,
        OrderType Type,
        OrderValidity OrderValidity,
        Side Side,
        int Quantity,
        int FilledQuantity,
        int RemainingQuantity,
        decimal? Price,
        decimal? TriggerPrice,
        DateOnly? GoodTilDate = null
    );
}