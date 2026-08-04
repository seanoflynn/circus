using Circus.Events;

namespace Circus.MarketData;

// Full order-by-order (L3) view of the working book, derived purely from the
// OrderConfirmedEvent stream - no IOrderBook access, no snapshotting. A consumer replays the
// returned deltas in order onto its own mirrored book, same as a real incremental feed.
//
// Stop orders (still Hidden) are excluded entirely, same as a real market's undisplayed
// contingent orders - a stop activating into a working limit order is reported as Added, not
// Modified, since it has no prior presence in the working book to "move" from.
//
// Quantity is always DisplayedQuantity, never RemainingQuantity - an iceberg's hidden reserve
// must never appear on a public depth feed.
//
// An update that lost time priority (a reprice, a quantity increase, or an iceberg peak
// refilling from its hidden reserve - see UpdateOrderConfirmed.PreviousExchangeOrderId) is
// reported as Removed(old id) + Added(new id), not Modified - a real order-by-order feed
// shows this as the old id leaving the book and a new one arriving at the back of the queue,
// since a consumer reconstructing time priority needs to see that, not just a quantity/price
// change against an id that kept its place.
//
// Everything one action did goes out as a single message, the way the by-price feed does: a
// sweep across several orders is one book update rather than several, so a consumer never reads
// a half-applied one and every sequence number marks a coherent book.
public class MarketByOrderIncrementalProducer : IIncrementalProducer<MarketByOrderDeltaEvent>
{
    public IList<MarketByOrderDeltaEvent> Process(IReadOnlyList<OrderBookEvent> events)
    {
        List<MarketByOrderDelta>? changes = null;

        foreach (var ev in events)
        {
            // One per side of a trade, arriving as two top-level events rather than one wrapping
            // both, so each becomes its own entry - paired by the TradeId they share.
            if (ev is FillOrderConfirmed fill)
            {
                Add(ref changes, new MarketByOrderDelta(fill.Order.Side, fill.Order.ExchangeOrderId,
                    fill.Order.Price!.Value, fill.Quantity, MarketByOrderDeltaAction.Filled, fill.TradeId));
                continue;
            }

            if (ev is UpdateOrderConfirmed {PreviousPrice: {} movedFromPrice} moved)
            {
                if (moved.PreviousExchangeOrderId != moved.Order.ExchangeOrderId)
                {
                    Add(ref changes, new MarketByOrderDelta(moved.Order.Side, moved.PreviousExchangeOrderId,
                        movedFromPrice, moved.PreviousQuantity, MarketByOrderDeltaAction.Removed));
                    Add(ref changes, new MarketByOrderDelta(moved.Order.Side, moved.Order.ExchangeOrderId,
                        moved.Order.Price!.Value, moved.Order.DisplayedQuantity,
                        MarketByOrderDeltaAction.Added));
                }
                else
                {
                    Add(ref changes, new MarketByOrderDelta(moved.Order.Side, moved.Order.ExchangeOrderId,
                        moved.Order.Price!.Value, moved.Order.DisplayedQuantity,
                        MarketByOrderDeltaAction.Modified));
                }

                continue;
            }

            MarketByOrderDelta? delta = ev switch
            {
                CreateOrderConfirmed {Order.Status: not OrderStatus.Hidden} create =>
                    new MarketByOrderDelta(create.Order.Side, create.Order.ExchangeOrderId,
                        create.Order.Price!.Value, create.Order.DisplayedQuantity,
                        MarketByOrderDeltaAction.Added),

                UpdateOrderConfirmed {PreviousPrice: null, Order.Status: OrderStatus.Hidden} => null,

                UpdateOrderConfirmed {PreviousPrice: null} update =>
                    new MarketByOrderDelta(update.Order.Side, update.Order.ExchangeOrderId,
                        update.Order.Price!.Value, update.Order.DisplayedQuantity,
                        MarketByOrderDeltaAction.Added),

                CancelOrderConfirmed {PreviousPrice: {} previousPrice} cancel =>
                    new MarketByOrderDelta(cancel.Order.Side, cancel.Order.ExchangeOrderId,
                        previousPrice, cancel.PreviousQuantity, MarketByOrderDeltaAction.Removed),

                ExpireOrderConfirmed {PreviousPrice: {} previousPrice} expire =>
                    new MarketByOrderDelta(expire.Order.Side, expire.Order.ExchangeOrderId,
                        previousPrice, expire.PreviousQuantity, MarketByOrderDeltaAction.Removed),

                _ => null
            };

            if (delta != null)
                Add(ref changes, delta);
        }

        if (changes == null)
            return Array.Empty<MarketByOrderDeltaEvent>();

        // Both carried by every event in the batch: one dispatch is one instrument at one instant.
        var first = events[0];
        return new[] {new MarketByOrderDeltaEvent(first.Symbol, first.Time, changes)};
    }

    private static void Add(ref List<MarketByOrderDelta>? changes, MarketByOrderDelta delta)
    {
        changes ??= new List<MarketByOrderDelta>();
        changes.Add(delta);
    }
}
