using System;

namespace Circus
{
    public record Security(
        string Name,
        SecurityType Type,
        decimal TickSize,
        decimal TickValue,
        int MarketOrderProtectionTicks = 10,
        int? PriceBandTicks = null,
        int? VolatilityAuctionBandTicks = null,

        // How long a volatility auction lasts before the book resumes by itself. Null leaves it
        // standing until something ends it explicitly.
        TimeSpan? VolatilityAuctionDuration = null
    );
}