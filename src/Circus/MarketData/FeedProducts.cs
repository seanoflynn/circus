namespace Circus.MarketData;

[Flags]
public enum FeedProducts
{
    None = 0,

    ByPrice = 1,

    ByOrder = 2,

    Trades = 4,

    Status = 8,

    Indicative = 16,

    All = ByPrice | ByOrder | Trades | Status | Indicative
}
