namespace Circus
{
    public record Security(
        string Name,
        SecurityType Type,
        decimal TickSize,
        decimal TickValue,
        int MarketOrderProtectionTicks = 10
    );
}