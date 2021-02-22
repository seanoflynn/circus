using System;

namespace Circus
{
    public record Fill(
        DateTime Time,
        decimal Price,
        int Quantity);
}
