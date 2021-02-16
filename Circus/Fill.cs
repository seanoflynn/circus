using System;

namespace Circus
{
    public record Fill(
        Guid OrderId,
        DateTime Time,
        Side Side,
        decimal Price,
        int Quantity,
        bool IsAggressor);
}
