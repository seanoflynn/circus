using Circus.OrderBook;

namespace Circus.DataProducers;

public interface IDataProducer<T>
{
    IList<T> Process(IOrderBook book, IReadOnlyList<OrderBookEvent> events);
}
