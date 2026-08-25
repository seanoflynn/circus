using Circus.Actions;
using Circus.Events;
using Circus.Time;

namespace Circus;

public sealed class TimestampingOrderBook : IOrderBook
{
    private readonly IOrderBook _book;
    private readonly IClock _clock;

    public TimestampingOrderBook(IOrderBook book, IClock clock)
    {
        _book = book;
        _clock = clock;
    }

    public TimestampingOrderBook(Instrument instrument, IClock clock)
        : this(new OrderBook(instrument), clock)
    {
    }

    public string Symbol => _book.Symbol;

    public OrderBookStatus Status => _book.Status;

    public IReadOnlyList<OrderBookEvent> Process(OrderBookAction action) =>
        _book.Process(action with {Time = _clock.GetCurrentTime()});
}
