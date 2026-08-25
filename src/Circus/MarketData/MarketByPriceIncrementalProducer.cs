using Circus.Events;

namespace Circus.MarketData;

public class MarketByPriceIncrementalProducer : IIncrementalProducer<MarketByPriceDeltaEvent>
{
    private readonly int _depth;

    public MarketByPriceIncrementalProducer(int depth = OrderBook.DefaultPublishedDepth)
    {
        if (depth <= 0)
            throw new ArgumentOutOfRangeException(nameof(depth), depth,
                "a feed carrying no levels is not a by-price feed");

        _depth = depth;
    }

    public int Depth => _depth;

    public IList<MarketByPriceDeltaEvent> Process(IReadOnlyList<MarketEvent> events)
    {
        List<MarketByPriceDeltaEvent>? output = null;

        foreach (var ev in events)
        {
            if (ev is not LevelsChanged levels || levels.Depth != _depth)
                continue;

            var changes = new List<MarketByPriceDelta>(levels.Changes.Count);
            foreach (var change in levels.Changes)
            {
                changes.Add(new MarketByPriceDelta(change.Side, change.LevelIndex, change.Price,
                    change.Quantity, change.Count, ToAction(change.Action)));
            }

            output ??= new List<MarketByPriceDeltaEvent>();
            output.Add(new MarketByPriceDeltaEvent(levels.Symbol, levels.Time, levels.Depth, changes));
        }

        return output ?? (IList<MarketByPriceDeltaEvent>) Array.Empty<MarketByPriceDeltaEvent>();
    }

    private static MarketByPriceDeltaAction ToAction(LevelChangeAction action) => action switch
    {
        LevelChangeAction.Added => MarketByPriceDeltaAction.Added,
        LevelChangeAction.Modified => MarketByPriceDeltaAction.Modified,
        LevelChangeAction.Removed => MarketByPriceDeltaAction.Removed,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
    };
}
