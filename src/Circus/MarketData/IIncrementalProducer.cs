using Circus.Events;

namespace Circus.MarketData;

// Turns a book's events into what a subscriber sees. A pure function of the event stream and
// nothing else: no producer is handed the book, because everything a consumer knows is derived
// from the events rather than queried back out of it - which is also what lets market data be
// rebuilt from a journal of those events, with no books involved at all.
//
// Handed MarketEvent and nothing else. The book's other half - what happened to one participant's
// order, carrying the CompanyId and ClientOrderId that say whose it was - is addressed to that
// participant and never broadcast, and a producer that cannot see it cannot leak it. That used to
// be a rule each producer kept and a reflection test checked; it is the signature now.
//
// That holds for snapshots too, which is why there is no second interface here for them. A
// snapshot is a statement of current state, and the temptation is to build one by reading the
// book - but a snapshot produced that way leaves no trace in the event stream and so cannot be
// reproduced by replaying it, which costs more than it saves. Instead a snapshot tick dispatches
// an action like any other, and the book answers with an event carrying the image; the producer
// that publishes it is an ordinary implementation of this interface reading an ordinary event.
//
// Most implementations hold no state. Those that do - InstrumentStatusDataProducer accumulates a
// composite no single event carries, and the by-price producer tracks the window it last
// published - cannot rebuild it after a missed event, and are not expected to. That is what the
// snapshot feed is for, and it is how the real feeds solve the same problem: CME and Eurex both
// publish incremental changes on one stream and periodic full state on another, so a subscriber
// joining mid-session or recovering from a detected gap starts from something true rather than
// replaying a session it never saw.
//
// The join is made on a sequence number: a snapshot says which incremental message it is
// consistent as of - CME calls it LastMsgSeqNumProcessed - and a subscriber buffers the
// incremental stream, waits for a snapshot, applies it, discards the buffered messages up to and
// including that number, then applies the rest. The channel stamps it at publish time, since the
// channel is what knows its own sequence.
public interface IIncrementalProducer<T> where T : MarketDataEvent
{
    IList<T> Process(IReadOnlyList<MarketEvent> events);
}
