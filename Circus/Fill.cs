using System;

namespace Circus
{
    public record Fill(
        Order Order,
        DateTime Time,
        decimal Price,
        int Quantity,
        bool IsAggressor);
}
