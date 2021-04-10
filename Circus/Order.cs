using System;

namespace Circus
{
    public record Order(
        Guid ClientId,
        Guid OrderId,
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
        decimal? TriggerPrice
    );
}