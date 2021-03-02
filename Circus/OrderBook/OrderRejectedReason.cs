namespace Circus.OrderBook
{
    public enum OrderRejectedReason
    {
        MarketClosed,
        MarketPreOpen,
        InvalidQuantity,
        InvalidPriceIncrement,
        OrderNotInBook,
        TooLateToCancel,
        NoOrdersToMatchMarketOrder,
        // PendingCancelOrReplace,
        // PriceExceedsCurrentPrice,
        // PriceExceedsCurrentPriceBand,
        // PriceOutsideLimits,
        // PriceOutsideBands,
        // InvalidExpireDate,
        // InvalidDisclosedQuantity,
        // InvalidStopPriceMustBeGreaterThanEqualTriggerPrice,
        // InvalidStopPriceMustBeLessThanEqualTriggerPrice,
        // StopPriceMustBeLessThanLastTradePrice,
        // StopPriceMustBeGreaterThanLastTradePrice,
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