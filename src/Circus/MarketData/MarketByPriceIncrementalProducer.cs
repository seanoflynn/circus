using Circus.Events;

namespace Circus.MarketData;

// The by-price incremental feed, and nothing more than a translation: the book already worked out
// which levels moved, because its price ladders carry the running totals, so there is no second
// book to keep in step here and no state to lose.
//
// That is the whole point of the book publishing LevelChanged. A producer deriving aggregated
// depth from order events has to hold the book the subscriber is missing, which is what the
// previous LevelDataProducer did - and why it could never resync after a missed event.
public class MarketByPriceIncrementalProducer : IIncrementalProducer<MarketByPriceDeltaEvent>
{
    // One message per dispatch, carrying every level that moved, rather than one per level - see
    // MarketByPriceDeltaEvent for why a book update is the unit. The book reports the levels
    // singly and this composes them, which is the same division as fills and the trade print.
    //
    // A dispatch is one action's events, so gathering across the batch is gathering exactly what
    // that action did.
    public IList<MarketByPriceDeltaEvent> Process(IReadOnlyList<OrderBookEvent> events)
    {
        List<MarketByPriceDelta>? changes = null;

        foreach (var ev in events)
        {
            if (ev is not LevelChanged level)
                continue;

            changes ??= new List<MarketByPriceDelta>();
            changes.Add(new MarketByPriceDelta(level.Side, level.LevelIndex, level.Price, level.Quantity,
                level.Count, ToAction(level.Action)));
        }

        if (changes == null)
            return Array.Empty<MarketByPriceDeltaEvent>();

        // Both carried by every event in the batch: one dispatch is one instrument at one instant.
        var first = events[0];
        return new[] {new MarketByPriceDeltaEvent(first.Symbol, first.Time, changes)};
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
