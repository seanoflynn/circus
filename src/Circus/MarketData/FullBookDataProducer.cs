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
public class FullBookDataProducer : IDataProducer<OrderBookDeltaEvent>
{
    public IList<OrderBookDeltaEvent> Process(IReadOnlyList<OrderBookEvent> events)
    {
        List<OrderBookDeltaEvent>? output = null;

        foreach (var ev in events)
        {
            // One per side of a trade, arriving as two top-level events rather than one wrapping
            // both, so each becomes its own delta.
            if (ev is FillOrderConfirmed fill)
            {
                Add(ref output, new OrderBookDeltaEvent(fill.Symbol, fill.Time, fill.Order.Side,
                    fill.Order.ExchangeOrderId, fill.Order.Price!.Value, fill.Quantity,
                    OrderBookDeltaAction.Filled));
                continue;
            }

            if (ev is UpdateOrderConfirmed {PreviousPrice: {} movedFromPrice} moved)
            {
                if (moved.PreviousExchangeOrderId != moved.Order.ExchangeOrderId)
                {
                    Add(ref output, new OrderBookDeltaEvent(moved.Symbol, moved.Time, moved.Order.Side,
                        moved.PreviousExchangeOrderId, movedFromPrice, moved.PreviousQuantity,
                        OrderBookDeltaAction.Removed));
                    Add(ref output, new OrderBookDeltaEvent(moved.Symbol, moved.Time, moved.Order.Side,
                        moved.Order.ExchangeOrderId, moved.Order.Price!.Value, moved.Order.DisplayedQuantity,
                        OrderBookDeltaAction.Added));
                }
                else
                {
                    Add(ref output, new OrderBookDeltaEvent(moved.Symbol, moved.Time, moved.Order.Side,
                        moved.Order.ExchangeOrderId, moved.Order.Price!.Value, moved.Order.DisplayedQuantity,
                        OrderBookDeltaAction.Modified));
                }

                continue;
            }

            OrderBookDeltaEvent? delta = ev switch
            {
                CreateOrderConfirmed {Order.Status: not OrderStatus.Hidden} create =>
                    new OrderBookDeltaEvent(create.Symbol, create.Time, create.Order.Side,
                        create.Order.ExchangeOrderId, create.Order.Price!.Value,
                        create.Order.DisplayedQuantity, OrderBookDeltaAction.Added),

                UpdateOrderConfirmed {PreviousPrice: null, Order.Status: OrderStatus.Hidden} => null,

                UpdateOrderConfirmed {PreviousPrice: null} update =>
                    new OrderBookDeltaEvent(update.Symbol, update.Time, update.Order.Side,
                        update.Order.ExchangeOrderId, update.Order.Price!.Value,
                        update.Order.DisplayedQuantity, OrderBookDeltaAction.Added),

                CancelOrderConfirmed {PreviousPrice: {} previousPrice} cancel =>
                    new OrderBookDeltaEvent(cancel.Symbol, cancel.Time, cancel.Order.Side,
                        cancel.Order.ExchangeOrderId, previousPrice, cancel.PreviousQuantity,
                        OrderBookDeltaAction.Removed),

                ExpireOrderConfirmed {PreviousPrice: {} previousPrice} expire =>
                    new OrderBookDeltaEvent(expire.Symbol, expire.Time, expire.Order.Side,
                        expire.Order.ExchangeOrderId, previousPrice, expire.PreviousQuantity,
                        OrderBookDeltaAction.Removed),

                _ => null
            };

            if (delta != null)
                Add(ref output, delta);
        }

        return output ?? (IList<OrderBookDeltaEvent>) Array.Empty<OrderBookDeltaEvent>();
    }

    private static void Add(ref List<OrderBookDeltaEvent>? output, OrderBookDeltaEvent delta)
    {
        output ??= new List<OrderBookDeltaEvent>();
        output.Add(delta);
    }
}
