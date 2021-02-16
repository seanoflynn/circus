namespace Circus.Enums
{
    public enum RejectReason
    {
        MarketClosed,
        InvalidQuantity,
        InvalidPriceIncrement,
        OrderNotInBook,
        TooLateToCancel,
        // PendingCancelOrReplace,
        // PriceExceedsCurrentPrice,
        // PriceExceedsCurrentPriceBand,
        // NoOrdersToMatchMarketOrder,
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