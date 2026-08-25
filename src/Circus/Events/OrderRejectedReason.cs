namespace Circus.Events;

public enum OrderRejectedReason
{
    MarketClosed,

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
    MaxVisibleQuantityOutOfRange,

    // Appended rather than filed with the other TriggerPrice reasons: no existing value may move.
    TriggerPriceTooFarFromPrice,

    BeyondDailyPriceLimit
}
