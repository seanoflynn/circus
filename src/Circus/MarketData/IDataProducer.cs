using Circus.OrderBook;
using Circus.OrderBook.Events;

namespace Circus.MarketData;

public interface IDataProducer<T>
{
    IList<T> Process(IOrderBook book, IReadOnlyList<OrderBookEvent> events);
}
