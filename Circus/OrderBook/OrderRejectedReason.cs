namespace Circus.OrderBook
{
    public enum OrderRejectedReason
    {
        MarketClosed,
        MarketPreOpen,
        InvalidQuantity,
        InvalidPriceIncrement,
        OrderNotInBook,
        OrderInBook,
        TooLateToCancel,
        NoOrdersToMatchMarketOrder,
        NoLastTradedPrice,
        TriggerPriceMustBeLessThanLastTradedPrice,
        TriggerPriceMustBeGreaterThanLastTradedPrice,
        TriggerPriceMustBeLessThanPrice,
        TriggerPriceMustBeGreaterThanPrice,
        NoChange,
        // PendingCancelOrReplace,
        // PriceExceedsCurrentPrice,
        // PriceExceedsCurrentPriceBand,
        // PriceOutsideLimits,
        // PriceOutsideBands,
        // InvalidExpireDate,
        // QuantityOutOfRange,
        // TypeMarketPreOpenPostClose,
        // TypeNotPermitted,
        // InstrumentHasRequestForCrossInProgress,
        // InvalidSessionDate,
        // MarketPaused,
        // MarketNoCancel,
        // MarketReserved,
        // MarketForbidden
    }
}