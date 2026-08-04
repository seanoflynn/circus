namespace Circus.MarketData;

// One order resting in the working book, as a subscriber sees it - the by-order counterpart of
// Level, and publishable for the same reason: an id, a price, a size, and nothing identifying who
// sent it.
//
// Quantity is displayed size, never remaining: an iceberg's hidden reserve is not on the book.
//
// Queue position is the order these arrive in rather than a number on each, exactly as it is in
// the book itself - a position is a fact about a level at an instant, not a property of an order,
// and numbering them would invite a consumer to trust one after the next message moved it.
public record RestingOrder(Side Side, string ExchangeOrderId, decimal Price, int Quantity);
