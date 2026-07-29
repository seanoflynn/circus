using Circus.OrderBook;
using Circus.OrderBook.Events;

namespace Circus.DataProducers;

public class TradeDataProducer : IDataProducer<TradedDataEvent>
{
    public IList<TradedDataEvent> Process(IOrderBook book, IReadOnlyList<OrderBookEvent> events)
    {
        List<TradedDataEvent>? output = null;

        foreach (var ev in events)
        {
            if (ev is OrdersMatched matched)
            {
                output ??= new List<TradedDataEvent>();
                output.Add(new TradedDataEvent(matched.Time, matched.Price, matched.Quantity));
            }
        }

        return output ?? (IList<TradedDataEvent>) Array.Empty<TradedDataEvent>();
    }
}
