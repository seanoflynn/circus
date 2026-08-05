using Circus.Events;

namespace Circus.MarketData;

// The by-order incremental feed, and nothing more than a translation: the book already worked out
// what changed about the displayed book and reported it as one update.
//
// It used to derive that itself, from the private order confirmations - including the rule that an
// update losing time priority is the old id leaving and a new one arriving rather than a modify in
// place. That rule is about a requeue the book performed, reconstructed a step later by reading
// PreviousExchangeOrderId off a confirmation addressed to someone else. It lives in the book now,
// for the reason the depth derivation moved there: the book is where the queue actually moved, and
// a producer reading a participant's confirmations is reading something no subscriber is entitled
// to see.
//
// A dispatch is one action's events, and the book emits at most one OrdersChanged for it, so the
// loop below normally runs once. It is a loop rather than a lookup because a dispatch spanning
// instruments would carry one per book, and each has to become its own message.
public class MarketByOrderIncrementalProducer : IIncrementalProducer<MarketByOrderDeltaEvent>
{
    public IList<MarketByOrderDeltaEvent> Process(IReadOnlyList<MarketEvent> events)
    {
        List<MarketByOrderDeltaEvent>? output = null;

        foreach (var ev in events)
        {
            if (ev is not OrdersChanged orders)
                continue;

            var changes = new List<MarketByOrderDelta>(orders.Changes.Count);
            foreach (var change in orders.Changes)
            {
                changes.Add(new MarketByOrderDelta(change.Side, change.ExchangeOrderId, change.Price,
                    change.Quantity, ToAction(change.Action), change.TradeId));
            }

            output ??= new List<MarketByOrderDeltaEvent>();
            output.Add(new MarketByOrderDeltaEvent(orders.Symbol, orders.Time, changes));
        }

        return output ?? (IList<MarketByOrderDeltaEvent>) Array.Empty<MarketByOrderDeltaEvent>();
    }

    // The two enums stay separate for the reason the by-price pair do: what a book says about
    // itself and what a subscriber is told are different vocabularies, and a change to one should
    // not be forced on the other.
    private static MarketByOrderDeltaAction ToAction(OrderChangeAction action) => action switch
    {
        OrderChangeAction.Added => MarketByOrderDeltaAction.Added,
        OrderChangeAction.Modified => MarketByOrderDeltaAction.Modified,
        OrderChangeAction.Removed => MarketByOrderDeltaAction.Removed,
        OrderChangeAction.Filled => MarketByOrderDeltaAction.Filled,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
    };
}
