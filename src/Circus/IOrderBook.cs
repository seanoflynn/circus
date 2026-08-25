using Circus.Actions;
using Circus.Events;

namespace Circus;

public interface IOrderBook
{
    string Symbol { get; }

    OrderBookStatus Status { get; }

    IReadOnlyList<OrderBookEvent> Process(OrderBookAction action);
}
