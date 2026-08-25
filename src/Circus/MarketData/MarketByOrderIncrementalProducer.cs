using Circus.Events;

namespace Circus.MarketData;

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

    private static MarketByOrderDeltaAction ToAction(OrderChangeAction action) => action switch
    {
        OrderChangeAction.Added => MarketByOrderDeltaAction.Added,
        OrderChangeAction.Modified => MarketByOrderDeltaAction.Modified,
        OrderChangeAction.Removed => MarketByOrderDeltaAction.Removed,
        OrderChangeAction.Filled => MarketByOrderDeltaAction.Filled,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
    };
}
