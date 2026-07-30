using Circus.Actions;

namespace Circus.Events;

public enum OrderCancelledReason
{
    Cancelled,
    UpdatedQuantityLowerThanFilledQuantity,
    NoOrdersToMatchMarketOrder,
    ImmediateOrCancelNotFilled,
    SelfMatchPrevention
}
