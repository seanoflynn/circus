using Circus.Events;

namespace Circus.MarketData;

public class MarketByPriceSnapshotProducer : IIncrementalProducer<LevelsDataEvent>
{
    private readonly int _depth;

    public MarketByPriceSnapshotProducer(int depth = OrderBook.DefaultPublishedDepth)
    {
        if (depth <= 0)
            throw new ArgumentOutOfRangeException(nameof(depth), depth,
                "a feed carrying no levels is not a by-price feed");

        _depth = depth;
    }

    public int Depth => _depth;

    public IList<LevelsDataEvent> Process(IReadOnlyList<MarketEvent> events)
    {
        List<LevelsDataEvent>? output = null;

        foreach (var ev in events)
        {
            if (ev is not BookSnapshot snapshot)
                continue;

            output ??= new List<LevelsDataEvent>();
            output.Add(new LevelsDataEvent(snapshot.Symbol, snapshot.Time, _depth,
                Truncate(snapshot.Bids), Truncate(snapshot.Offers)));
        }

        return output ?? (IList<LevelsDataEvent>) Array.Empty<LevelsDataEvent>();
    }

    private IReadOnlyList<Level> Truncate(IReadOnlyList<Level> levels)
    {
        if (levels.Count <= _depth)
            return levels;

        var window = new List<Level>(_depth);
        for (var i = 0; i < _depth; i++)
            window.Add(levels[i]);

        return window;
    }
}

public class InstrumentStatusSnapshotProducer : IIncrementalProducer<InstrumentStatusDataEvent>
{
    public IList<InstrumentStatusDataEvent> Process(IReadOnlyList<MarketEvent> events)
    {
        List<InstrumentStatusDataEvent>? output = null;

        foreach (var ev in events)
        {
            if (ev is not BookSnapshot snapshot)
                continue;

            output ??= new List<InstrumentStatusDataEvent>();
            output.Add(new InstrumentStatusDataEvent(snapshot.Symbol, snapshot.Time, snapshot.Status,
                snapshot.StatusReason, snapshot.ResumesAt, snapshot.LimitState));
        }

        return output ?? (IList<InstrumentStatusDataEvent>) Array.Empty<InstrumentStatusDataEvent>();
    }
}

public class IndicativePriceSnapshotProducer : IIncrementalProducer<IndicativePriceDataEvent>
{
    public IList<IndicativePriceDataEvent> Process(IReadOnlyList<MarketEvent> events)
    {
        List<IndicativePriceDataEvent>? output = null;

        foreach (var ev in events)
        {
            if (ev is not BookSnapshot snapshot)
                continue;

            output ??= new List<IndicativePriceDataEvent>();
            output.Add(new IndicativePriceDataEvent(snapshot.Symbol, snapshot.Time,
                snapshot.IndicativePrice, snapshot.IndicativeQuantity));
        }

        return output ?? (IList<IndicativePriceDataEvent>) Array.Empty<IndicativePriceDataEvent>();
    }
}

public class MarketByOrderSnapshotProducer : IIncrementalProducer<OrdersDataEvent>
{
    public IList<OrdersDataEvent> Process(IReadOnlyList<MarketEvent> events)
    {
        List<OrdersDataEvent>? output = null;

        foreach (var ev in events)
        {
            if (ev is not BookSnapshot snapshot)
                continue;

            output ??= new List<OrdersDataEvent>();
            output.Add(new OrdersDataEvent(snapshot.Symbol, snapshot.Time, snapshot.Orders));
        }

        return output ?? (IList<OrdersDataEvent>) Array.Empty<OrdersDataEvent>();
    }
}
