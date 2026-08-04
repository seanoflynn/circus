namespace Circus.Agents;

// One order an agent believes it still has at the venue, as of the last event the venue sent it
// about that order.
//
// A flattened copy of the fields an agent actually decides on, rather than the Order the events
// carry. Order is the book's view - it names an exchange order id that moves under an agent's
// feet, a created time, a validity - and an agent asking "what am I quoting, and where" wants
// none of that. Keeping the two apart also means this can hold something Order does not, which
// is the point of tracking rather than caching.
//
// Status distinguishes a stop order still waiting for its trigger (Hidden) from one resting in
// the working book (Working): both are live, only one is quoting.
public sealed record LiveOrder(
    string Symbol,
    string CompanyId,
    string ClientOrderId,
    Side Side,
    OrderStatus Status,
    int Quantity,
    int RemainingQuantity,
    int DisplayedQuantity,
    decimal? Price,
    decimal? TriggerPrice
);
