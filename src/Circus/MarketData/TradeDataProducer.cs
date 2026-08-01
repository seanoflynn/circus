using Circus.Events;

namespace Circus.MarketData;

public class TradeDataProducer : IDataProducer<TradeDataEvent>
{
    public IList<TradeDataEvent> Process(IReadOnlyList<OrderBookEvent> events)
    {
        List<TradeDataEvent>? output = null;

        foreach (var ev in events)
        {
            if (ev is OrdersMatched matched)
            {
                output ??= new List<TradeDataEvent>();
                output.Add(new TradeDataEvent(matched.Symbol, matched.Time, matched.Price, matched.Quantity));
            }
        }

        return output ?? (IList<TradeDataEvent>) Array.Empty<TradeDataEvent>();
    }
}
