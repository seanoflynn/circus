using System;
using System.Collections.Generic;
using System.Linq;
using Circus.OrderBook;

namespace Circus.DataProducers
{
    // Maintains its own view of working-book price levels purely from the OrderConfirmedEvent
    // stream, rather than querying IOrderBook.GetLevels - so one instance is required per
    // IOrderBook, created before that book processes its first action, and it can never resync
    // after a missed event (there's no snapshot fallback).
    //
    // Tracks DisplayedQuantity, not RemainingQuantity - an iceberg's hidden reserve must never
    // appear in market data. Known gap: when an iceberg's displayed peak is exhausted mid-fill
    // and immediately replenished (InMemoryOrderBook.FillOrder/InternalOrder.Replenish), that
    // replenishment happens silently within the same Match() pass with no event of its own - the
    // Fill event this producer sees only carries the traded quantity, not the fact that the
    // order's displayed size just jumped back up. The level will under-report until the next
    // event touches that order.
    public class LevelDataProducer : IDataProducer<LevelsDataEvent>
    {
        private class LevelState
        {
            public int Quantity;
            public int Count;
        }

        private readonly int _maxLevels;
        private readonly Dictionary<Side, SortedDictionary<decimal, LevelState>> _levels = new()
        {
            {Side.Buy, new SortedDictionary<decimal, LevelState>(Comparer<decimal>.Create((a, b) => b.CompareTo(a)))},
            {Side.Sell, new SortedDictionary<decimal, LevelState>()}
        };

        public LevelDataProducer(int maxLevels)
        {
            _maxLevels = maxLevels;
        }

        public IList<LevelsDataEvent> Process(IOrderBook book, IReadOnlyList<OrderBookEvent> events)
        {
            if (events.Count == 0)
                return Array.Empty<LevelsDataEvent>();

            foreach (var ev in events)
            {
                switch (ev)
                {
                    case CreateOrderConfirmed create:
                        if (create.Order.Status != OrderStatus.Hidden)
                            Add(create.Order.Side, create.Order.Price!.Value, create.Order.DisplayedQuantity);
                        break;

                    case UpdateOrderConfirmed update:
                        if (update.PreviousPrice.HasValue)
                            Remove(update.Order.Side, update.PreviousPrice.Value, update.PreviousQuantity);
                        if (update.Order.Status != OrderStatus.Hidden)
                            Add(update.Order.Side, update.Order.Price!.Value, update.Order.DisplayedQuantity);
                        break;

                    case CancelOrderConfirmed cancel:
                        if (cancel.PreviousPrice.HasValue)
                            Remove(cancel.Order.Side, cancel.PreviousPrice.Value, cancel.PreviousQuantity);
                        break;

                    case ExpireOrderConfirmed expire:
                        if (expire.PreviousPrice.HasValue)
                            Remove(expire.Order.Side, expire.PreviousPrice.Value, expire.PreviousQuantity);
                        break;

                    // FillOrderConfirmed is only ever nested inside OrdersMatched.Fills, never a
                    // top-level event in its own right.
                    case OrdersMatched matched:
                        foreach (var fill in matched.Fills)
                        {
                            ReduceQuantity(fill.Order.Side, fill.Order.Price!.Value, fill.Quantity,
                                fullyFilled: fill.Order.RemainingQuantity == 0);
                        }

                        break;
                }
            }

            var bids = Snapshot(Side.Buy);
            var offers = Snapshot(Side.Sell);
            return new[] {new LevelsDataEvent(events[0].Time, bids, offers)};
        }

        private void Add(Side side, decimal price, int quantity)
        {
            var levels = _levels[side];
            if (levels.TryGetValue(price, out var level))
            {
                level.Quantity += quantity;
                level.Count++;
            }
            else
            {
                levels[price] = new LevelState {Quantity = quantity, Count = 1};
            }
        }

        private void Remove(Side side, decimal price, int quantity)
        {
            var levels = _levels[side];
            var level = levels[price];
            level.Quantity -= quantity;
            level.Count--;
            if (level.Count == 0)
                levels.Remove(price);
        }

        private void ReduceQuantity(Side side, decimal price, int quantity, bool fullyFilled)
        {
            var levels = _levels[side];
            var level = levels[price];
            level.Quantity -= quantity;
            if (fullyFilled)
            {
                level.Count--;
                if (level.Count == 0)
                    levels.Remove(price);
            }
        }

        private IReadOnlyList<Level> Snapshot(Side side) =>
            _levels[side].Take(_maxLevels)
                .Select(kv => new Level(kv.Key, kv.Value.Quantity, kv.Value.Count))
                .ToList();
    }

    public record LevelsDataEvent(DateTime Time, IReadOnlyList<Level> Bids, IReadOnlyList<Level> Offers);
}
