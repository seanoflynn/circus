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
        decimal Price,
        decimal? StopPrice,
        int Quantity,
        int FilledQuantity,
        int RemainingQuantity
    );
}