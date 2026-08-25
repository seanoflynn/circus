using Circus.Events;

namespace Circus.MarketData;

public class TradeDataProducer : IIncrementalProducer<TradeDataEvent>
{
    public IList<TradeDataEvent> Process(IReadOnlyList<MarketEvent> events)
    {
        List<TradeDataEvent>? output = null;

        foreach (var ev in events)
        {
            if (ev is not TradePrinted trade)
                continue;

            output ??= new List<TradeDataEvent>();
            output.Add(new TradeDataEvent(trade.Symbol, trade.Time, trade.TradeId, trade.Price,
                trade.Quantity));
        }

        return output ?? (IList<TradeDataEvent>) Array.Empty<TradeDataEvent>();
    }
}
