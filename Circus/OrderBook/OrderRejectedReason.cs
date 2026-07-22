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
        OrderIdAlreadyUsed,
        TooLateToCancel,
        NoOrdersToMatchMarketOrder,
        NoLastTradedPrice,
        TriggerPriceMustBeLessThanLastTradedPrice,
        TriggerPriceMustBeGreaterThanLastTradedPrice,
        TriggerPriceMustBeLessThanPrice,
        TriggerPriceMustBeGreaterThanPrice,
        NoChange,
        InsufficientLiquidityForFillOrKill,
        InvalidExpireDate,
        GoodTilDateRequired,
        // PendingCancelOrReplace,
        // PriceExceedsCurrentPrice,
        // PriceExceedsCurrentPriceBand,
        // PriceOutsideLimits,
        // PriceOutsideBands,
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