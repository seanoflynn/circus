using Circus.OrderBook.Actions;
using Circus.OrderBook.Events;

namespace Circus.OrderBook;

// Actions in, events out. Everything a consumer knows about the book - price levels, trades,
// an auction's indicative quote - is derived from the event stream rather than queried back
// out of the book, so a market data feed and a downstream mirror see the same thing.
public interface IOrderBook
{
    Security Security { get; }

    OrderBookStatus Status { get; }

    IReadOnlyList<OrderBookEvent> Process(OrderBookAction action);
}
