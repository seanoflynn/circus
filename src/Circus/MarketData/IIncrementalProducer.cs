using Circus.Events;

namespace Circus.MarketData;

public interface IIncrementalProducer<T> where T : MarketDataEvent
{
    IList<T> Process(IReadOnlyList<MarketEvent> events);
}
