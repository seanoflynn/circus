using System;

namespace Circus
{
    public record Order(
        Guid Id,
        Security Security,
        DateTime CreatedTime,
        DateTime ModifiedTime,
        DateTime? CompletedTime,
        OrderStatus Status,
        OrderType Type,
        TimeInForce TimeInForce,
        Side Side,
        decimal Price,
        decimal? StopPrice,
        int Quantity,
        int FilledQuantity,
        int RemainingQuantity
    );
}