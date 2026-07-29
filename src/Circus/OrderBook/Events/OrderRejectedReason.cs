namespace Circus.OrderBook.Events;

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
    MaxVisibleQuantityOutOfRange,

    // The trigger and limit prices are on the right sides of each other but too far apart: a
    // stop elected this far from its trigger would rest where the band would never have
    // accepted it directly. Appended rather than filed with the other TriggerPrice reasons, so
    // that no existing member's numeric value moves.
    TriggerPriceTooFarFromPrice,

    // Beyond the session's ceiling or floor. Distinct from PriceOutsideBands because the two
    // mean different things to whoever sent the order: a band moves with the market and will
    // very likely accept the same price shortly, a daily limit stands for the session.
    BeyondDailyPriceLimit
}
