using Circus.Actions;
using Circus.Events;
using Circus.Time;

namespace Circus;

// The boundary where wall-clock time enters. An OrderBook reads no clock, so something has to
// say when each action happened; this stamps every action on the way in and is the only part of
// the pipeline allowed to be nondeterministic.
//
// Wrapping a book is what a gateway does at a real venue: the exchange stamps its own arrival
// time on an inbound message and the matching engine works off that stamp, never off whatever
// the clock says at the moment it gets round to the message. Everything downstream of here is a
// pure function of the actions it is handed, which is what lets a journal of those actions
// rebuild a book by replaying them with no clock involved at all.
//
// An action that already carries a Time is stamped over, not honoured: a book driven off a
// clock has one source of truth for what time it is, and it is this one. Feed pre-stamped
// actions - a replay, or a driver with its own schedule - straight to the book instead.
public sealed class TimestampingOrderBook : IOrderBook
{
    private readonly IOrderBook _book;
    private readonly IClock _clock;

    public TimestampingOrderBook(IOrderBook book, IClock clock)
    {
        _book = book;
        _clock = clock;
    }

    public TimestampingOrderBook(Security security, IClock clock)
        : this(new OrderBook(security), clock)
    {
    }

    public Security Security => _book.Security;

    public OrderBookStatus Status => _book.Status;

    // One reading per action, so every event the action produces shares an instant.
    public IReadOnlyList<OrderBookEvent> Process(OrderBookAction action) =>
        _book.Process(action with {Time = _clock.GetCurrentTime()});
}
