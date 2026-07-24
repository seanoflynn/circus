using System;
using System.Collections.Generic;
using Circus.OrderBook;

namespace Circus.DataProducers
{
    public enum OrderBookDeltaAction
    {
        Added,
        Modified,
        Removed,
        Filled
    }

    // ExchangeOrderId only - never CompanyId/ClientOrderId, which identify the originating
    // client and must not be broadcast on a public depth feed.
    public record OrderBookDeltaEvent(DateTime Time, Side Side, string ExchangeOrderId, decimal Price, int Quantity,
        OrderBookDeltaAction Action);

    // Full order-by-order (L3) view of the working book, derived purely from the
    // OrderConfirmedEvent stream - no IOrderBook access, no snapshotting. A consumer replays the
    // returned deltas in order onto its own mirrored book, same as a real incremental feed.
    //
    // Stop orders (still Hidden) are excluded entirely, same as a real market's undisplayed
    // contingent orders - a stop activating into a working limit order is reported as Added, not
    // Modified, since it has no prior presence in the working book to "move" from.
    public class OrderBookDeltaDataProducer : IDataProducer<OrderBookDeltaEvent>
    {
        public IList<OrderBookDeltaEvent> Process(IOrderBook book, IReadOnlyList<OrderBookEvent> events)
        {
            List<OrderBookDeltaEvent>? output = null;

            foreach (var ev in events)
            {
                // FillOrderConfirmed is only ever nested inside OrdersMatched.Fills, never a
                // top-level event in its own right.
                if (ev is OrdersMatched matched)
                {
                    foreach (var fill in matched.Fills)
                    {
                        Add(ref output, new OrderBookDeltaEvent(fill.Time, fill.Order.Side, fill.Order.ExchangeOrderId,
                            fill.Order.Price!.Value, fill.Quantity, OrderBookDeltaAction.Filled));
                    }

                    continue;
                }

                OrderBookDeltaEvent? delta = ev switch
                {
                    CreateOrderConfirmed {Order.Status: not OrderStatus.Hidden} create =>
                        new OrderBookDeltaEvent(create.Time, create.Order.Side, create.Order.ExchangeOrderId,
                            create.Order.Price!.Value, create.Order.RemainingQuantity, OrderBookDeltaAction.Added),

                    UpdateOrderConfirmed {PreviousPrice: null, Order.Status: OrderStatus.Hidden} => null,

                    UpdateOrderConfirmed {PreviousPrice: null} update =>
                        new OrderBookDeltaEvent(update.Time, update.Order.Side, update.Order.ExchangeOrderId,
                            update.Order.Price!.Value, update.Order.RemainingQuantity, OrderBookDeltaAction.Added),

                    UpdateOrderConfirmed update =>
                        new OrderBookDeltaEvent(update.Time, update.Order.Side, update.Order.ExchangeOrderId,
                            update.Order.Price!.Value, update.Order.RemainingQuantity, OrderBookDeltaAction.Modified),

                    CancelOrderConfirmed {PreviousPrice: {} previousPrice} cancel =>
                        new OrderBookDeltaEvent(cancel.Time, cancel.Order.Side, cancel.Order.ExchangeOrderId,
                            previousPrice, cancel.PreviousQuantity, OrderBookDeltaAction.Removed),

                    ExpireOrderConfirmed {PreviousPrice: {} previousPrice} expire =>
                        new OrderBookDeltaEvent(expire.Time, expire.Order.Side, expire.Order.ExchangeOrderId,
                            previousPrice, expire.PreviousQuantity, OrderBookDeltaAction.Removed),

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
}
