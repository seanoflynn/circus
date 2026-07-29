namespace Circus.OrderBook
{
    public enum OrderRejectedReason
    {
        MarketClosed,

        // The phase takes orders but not ones with no limit of their own - pre-open, and now a
        // pause or a halt too, none of which have a book to price a market order against.
        MarketOrdersNotAccepted,
        InvalidQuantity,
        InvalidPriceIncrement,
        OrderNotInBook,
        OrderInBook,
        OrderIdAlreadyUsed,
        TooLateToCancel,
        NoOrdersToMatchMarketOrder,
        NoLastTradedPrice,
        TriggerPriceMustBeLessThanLastTradedPrice,
        TriggerPriceMustBeGreaterThanLastTradedPrice,
        TriggerPriceMustBeLessThanPrice,
        TriggerPriceMustBeGreaterThanPrice,
        NoChange,
        InvalidExpireDate,
        ClientOrderIdRequired,
        ClientOrderIdTooLong,
        CompanyIdRequired,
        CompanyIdTooLong,
        SelfMatchPreventionIdTooLong,
        PriceOutsideBands,
        MinQuantityOutOfRange,
        InsufficientLiquidityForMinQuantity,
        MaxVisibleQuantityOutOfRange
    }
}