using Circus.Events;

namespace Circus.MarketData;

// Publishes the auction quote a book is running - CME's indicative opening price, Eurex's
// indicative auction price. Unlike the level producers this holds no state of its own: the
// book already emits IndicativePriceChanged only when the quote moves, so there is nothing
// here to deduplicate against.
//
// A null Price withdraws the quote (the book stopped crossing, or the auction ended), which a
// subscriber must publish as such rather than leaving the last price standing.
public class IndicativePriceDataProducer : IDataProducer<IndicativePriceDataEvent>
{
    public IList<IndicativePriceDataEvent> Process(IReadOnlyList<OrderBookEvent> events)
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
