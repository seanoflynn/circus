using System.Collections.Generic;
using Circus.OrderBook;

namespace Circus.DataProducers
{
    public interface IDataProducer<T>
    {
        IList<T> Process(IOrderBook book, IList<OrderBookEvent> events);
    }
}