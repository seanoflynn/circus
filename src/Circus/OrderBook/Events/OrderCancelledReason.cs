using Circus.OrderBook.Actions;

namespace Circus.OrderBook.Events;

public enum OrderCancelledReason
{
    Cancelled,
    UpdatedQuantityLowerThanFilledQuantity,
    NoOrdersToMatchMarketOrder,
    ImmediateOrCancelNotFilled,
    SelfMatchPrevention
}
