namespace Circus.OrderBook
{
    public enum OrderCancelledReason
    {
        Cancelled,
        UpdatedQuantityLowerThanFilledQuantity,
        NoOrdersToMatchMarketOrder
    }
}