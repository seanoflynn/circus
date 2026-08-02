using Circus.Events;

namespace Circus.MarketData;

// Turns a book's events into what a subscriber sees. A pure function of the event stream and
// nothing else: no producer is handed the book, because everything a consumer knows is derived
// from the events rather than queried back out of it - which is also what lets market data be
// rebuilt from a journal of those events, with no books involved at all.
//
// Most implementations are stateful, so one instance per instrument, created before that book
// processes its first action. None of them can resync after a missed event.
public interface IDataProducer<T> where T : MarketDataEvent
{
    IList<T> Process(IReadOnlyList<OrderBookEvent> events);
}
