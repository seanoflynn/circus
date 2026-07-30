using Circus.Actions;
using Circus.Events;

namespace Circus.Sequencing;

// One action handed to one book, and what came back. Sequence is the venue's dispatch count -
// the order things happened in, which is the only thing that exists at venue scope rather than
// per security.
//
// No book reference: Action.Security is which book this was, and every event carries its own
// security too, so a consumer keys off that the same way the dispatch loop does. Keeping the book
// itself out of the record is what lets this stay the same shape once one action can imply a fill
// in another.
public readonly record struct Dispatched(long Sequence, OrderBookAction Action,
    IReadOnlyList<OrderBookEvent> Events);
