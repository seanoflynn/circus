using Circus.Events;

namespace Circus.MarketData;

public class TradeDataProducer : IDataProducer<TradedDataEvent>
{
    public IList<TradedDataEvent> Process(IReadOnlyList<OrderBookEvent> events)
    {
        List<TradedDataEvent>? output = null;

        foreach (var ev in events)
        {
            if (ev is OrdersMatched matched)
            {
                output ??= new List<TradedDataEvent>();
                output.Add(new TradedDataEvent(matched.Security, matched.Time, matched.Price, matched.Quantity));
            }
        }

        return output ?? (IList<TradedDataEvent>) Array.Empty<TradedDataEvent>();
    }
}
