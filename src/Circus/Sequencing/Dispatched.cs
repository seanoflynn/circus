using Circus.Actions;
using Circus.Events;

namespace Circus.Sequencing;

public readonly record struct Dispatched(long Sequence, OrderBookAction Action,
    IReadOnlyList<OrderBookEvent> Events);
