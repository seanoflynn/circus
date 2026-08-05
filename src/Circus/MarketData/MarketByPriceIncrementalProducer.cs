using Circus.Events;

namespace Circus.MarketData;

// The by-price incremental feed, and nothing more than a translation: the book already worked out
// which levels moved and reported them as one update, because its price ladders carry the running
// totals. There is no second book to keep in step here and no state to lose.
//
// That is the whole point of the book publishing LevelsChanged. A producer deriving aggregated
// depth from order events has to hold the book the subscriber is missing, which is what the
// previous LevelDataProducer did - and why it could never resync after a missed event.
//
// A dispatch is one action's events, and the book emits at most one LevelsChanged for it, so the
// loop below normally runs once. It is a loop rather than a lookup because a dispatch spanning
// instruments would carry one per book, and each has to become its own message.
public class MarketByPriceIncrementalProducer : IIncrementalProducer<MarketByPriceDeltaEvent>
{
    public IList<MarketByPriceDeltaEvent> Process(IReadOnlyList<MarketEvent> events)
    {
        List<MarketByPriceDeltaEvent>? output = null;

        foreach (var ev in events)
        {
            if (ev is not LevelsChanged levels)
                continue;

            var changes = new List<MarketByPriceDelta>(levels.Changes.Count);
            foreach (var change in levels.Changes)
            {
                changes.Add(new MarketByPriceDelta(change.Side, change.LevelIndex, change.Price,
                    change.Quantity, change.Count, ToAction(change.Action)));
            }

            output ??= new List<MarketByPriceDeltaEvent>();
            output.Add(new MarketByPriceDeltaEvent(levels.Symbol, levels.Time, changes));
        }

        return output ?? (IList<MarketByPriceDeltaEvent>) Array.Empty<MarketByPriceDeltaEvent>();
    }

    // The two enums are deliberately separate rather than one shared between the book's events and
    // the messages a venue publishes, the same way FillOrderConfirmed and TradeDataEvent are: what
    // a book says about itself and what a subscriber is told are different vocabularies, and a
    // change to one should not be forced on the other.
    private static MarketByPriceDeltaAction ToAction(LevelChangeAction action) => action switch
    {
        LevelChangeAction.Added => MarketByPriceDeltaAction.Added,
        LevelChangeAction.Modified => MarketByPriceDeltaAction.Modified,
        LevelChangeAction.Removed => MarketByPriceDeltaAction.Removed,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
    };
}
