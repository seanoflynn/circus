using Circus.Events;

namespace Circus.MarketData;

// Everything a venue publishes about one instrument, assembled in one place. A book's events go
// in, the messages a subscriber would receive come out.
//
// One bundle per instrument, created before that book processes its first action: the level, depth
// and status producers inside it are each stateful and none can resync after a missed event.
//
// One level producer at one depth, unlike the several a caller might otherwise run side by side.
// Two LevelsDataEvent streams at different depths are indistinguishable once merged into a
// channel, so depth is a property of the feed rather than something stacked within it - which is
// also how a venue does it, publishing a five-deep and a ten-deep product as two feeds rather
// than one carrying both.
//
// A venue separating market-by-price from market-by-order would compose its own bundle rather
// than use this: both are here, which is the useful default for a simulator and more than a real
// depth feed carries.
public sealed class InstrumentFeed
{
    private readonly LevelDataProducer _levels;
    private readonly FullBookDataProducer _orderByOrder = new();
    private readonly TradeDataProducer _trades = new();
    private readonly IndicativePriceDataProducer _indicative = new();
    private readonly InstrumentStatusDataProducer _status = new();

    public InstrumentFeed(string symbol, int maxLevels)
    {
        Symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
        _levels = new LevelDataProducer(maxLevels);
    }

    public string Symbol { get; }

    // Ordering within one call is by producer, in the fixed order below, rather than interleaved
    // by time: every event in a single dispatch shares an instant, so there is no time order
    // among them to preserve. Across calls it is the order the venue dispatched them in, which is
    // the ordering that actually carries meaning.
    public IReadOnlyList<MarketDataEvent> Process(IReadOnlyList<OrderBookEvent> events)
    {
        if (events.Count == 0)
            return Array.Empty<MarketDataEvent>();

        List<MarketDataEvent>? output = null;

        Collect(ref output, _status.Process(events));
        Collect(ref output, _trades.Process(events));
        Collect(ref output, _levels.Process(events));
        Collect(ref output, _orderByOrder.Process(events));
        Collect(ref output, _indicative.Process(events));

        return output ?? (IReadOnlyList<MarketDataEvent>) Array.Empty<MarketDataEvent>();
    }

    private static void Collect<T>(ref List<MarketDataEvent>? output, IList<T> produced)
        where T : MarketDataEvent
    {
        if (produced.Count == 0)
            return;

        output ??= new List<MarketDataEvent>();
        foreach (var data in produced)
            output.Add(data);
    }
}