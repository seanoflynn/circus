using Circus.Events;

namespace Circus.MarketData;

// Turns a book's events into what a subscriber sees change. A pure function of the event stream
// and nothing else: no incremental producer is handed the book, because everything a consumer
// knows is derived from the events rather than queried back out of it - which is also what lets
// market data be rebuilt from a journal of those events, with no books involved at all.
//
// The counterpart is ISnapshotProducer, which is handed the book precisely because a snapshot
// cannot be derived that way. The split is the one CME and Eurex publish along: an incremental
// feed carrying changes, and a separate snapshot feed carrying state, with the second existing so
// a subscriber can start or restart against the first.
//
// Most implementations hold no state at all. Those that do (InstrumentStatusDataProducer, which
// accumulates a composite no single event carries) hold only what one instrument's stream implies,
// and the snapshot feed is what lets a subscriber recover it after a missed event.
public interface IIncrementalProducer<T> where T : MarketDataEvent
{
    IList<T> Process(IReadOnlyList<OrderBookEvent> events);
}
