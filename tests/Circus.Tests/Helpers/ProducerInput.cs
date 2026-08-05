using Circus.Events;
using Circus.MarketData;

namespace Circus.Tests.Helpers;

internal static class ProducerInput
{
    // A book's whole output handed to a producer, filtered to the half a venue broadcasts.
    //
    // Producers take MarketEvent so that a participant's own confirmations cannot reach a public
    // feed - the guarantee is the signature. InstrumentFeed does this filtering at its boundary;
    // this is the same step for a test driving a producer straight off a book, so what the
    // producer sees is what a real feed would have given it.
    //
    // An extension rather than an overload on the interface: the point is that only MarketEvent
    // gets in, and an overload taking everything would hand that back.
    public static IList<T> Process<T>(this IIncrementalProducer<T> producer,
        IReadOnlyList<OrderBookEvent> events)
        where T : MarketDataEvent =>
        producer.Process(events.OfType<MarketEvent>().ToList());
}
