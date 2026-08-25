using Circus.Events;

namespace Circus.MarketData;

public class IndicativePriceDataProducer : IIncrementalProducer<IndicativePriceDataEvent>
{
    public IList<IndicativePriceDataEvent> Process(IReadOnlyList<MarketEvent> events)
    {
        List<IndicativePriceDataEvent>? output = null;

        foreach (var ev in events)
        {
            if (ev is IndicativePriceChanged changed)
            {
                output ??= new List<IndicativePriceDataEvent>();
                output.Add(new IndicativePriceDataEvent(changed.Symbol, changed.Time, changed.Price, changed.Quantity));
            }
        }

        return output ?? (IList<IndicativePriceDataEvent>) Array.Empty<IndicativePriceDataEvent>();
    }
}
