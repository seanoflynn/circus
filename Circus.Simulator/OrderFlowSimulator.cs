using System;
using System.Collections.Generic;
using Circus.OrderBook;
using Circus.TimeProviders;

namespace Circus.Simulator
{
    // Generates a plausible, replayable stream of order book actions (create/cancel/update,
    // limit/market) for a single security. Drives a private "shadow" order book purely to keep
    // track of which orders are currently live, so that generated cancels/updates always target
    // a real resting order instead of a random, possibly-already-filled id.
    //
    // Pass a seed for a deterministic, reproducible trace (e.g. a committed benchmark baseline);
    // omit it to get a fresh random trace each run (e.g. fuzzing).
    public sealed class OrderFlowSimulator
    {
        private readonly Security _security;
        private readonly SimulatorOptions _options;
        private readonly Random _random;
        private readonly int _seed;

        private readonly InMemoryOrderBook _shadowBook;

        private readonly List<Guid> _liveIds = new();
        private readonly Dictionary<Guid, int> _liveIndex = new();
        private readonly Dictionary<Guid, LiveOrderInfo> _liveInfo = new();

        public OrderFlowSimulator(Security security, SimulatorOptions? options = null, int? seed = null)
        {
            _security = security;
            _options = options ?? new SimulatorOptions();
            _seed = seed ?? Random.Shared.Next();
            _random = new Random(_seed);

            _shadowBook = new InMemoryOrderBook(_security, new TestTimeProvider(DateTime.UtcNow));
            _shadowBook.UpdateStatus(OrderBookStatus.Open);
        }

        public int Seed => _seed;

        // Generates the next `actionCount` actions, continuing from wherever the internal
        // shadow book currently is (i.e. calling this repeatedly extends the same session
        // rather than restarting it). The returned list contains only the newly generated
        // batch, not the full history.
        public IReadOnlyList<OrderBookAction> Generate(int actionCount)
        {
            var actions = new List<OrderBookAction>(actionCount);

            for (var i = 0; i < actionCount; i++)
            {
                var action = NextAction();
                actions.Add(action);
                Apply(action);
            }

            return actions;
        }

        private void Apply(OrderBookAction action)
        {
            var events = _shadowBook.Process(action);
            foreach (var e in events)
            {
                switch (e)
                {
                    case CreateOrderConfirmed c:
                        Track(c.Order);
                        break;
                    case UpdateOrderConfirmed u:
                        Track(u.Order);
                        break;
                    case CancelOrderConfirmed cancel:
                        RemoveLive(cancel.Order.OrderId);
                        break;
                    case ExpireOrderConfirmed expire:
                        RemoveLive(expire.Order.OrderId);
                        break;
                    case OrdersMatched matched:
                        foreach (var fill in matched.Fills)
                        {
                            if (fill.Order.RemainingQuantity == 0)
                                RemoveLive(fill.Order.OrderId);
                        }
                        break;
                }
            }
        }

        private void Track(Order order)
        {
            if (order.RemainingQuantity == 0)
            {
                RemoveLive(order.OrderId);
                return;
            }

            if (!_liveIndex.ContainsKey(order.OrderId))
            {
                _liveIndex[order.OrderId] = _liveIds.Count;
                _liveIds.Add(order.OrderId);
            }

            _liveInfo[order.OrderId] = new LiveOrderInfo(order.ClientId, order.Side, order.Price, order.TriggerPrice);
        }

        private void RemoveLive(Guid orderId)
        {
            if (!_liveIndex.TryGetValue(orderId, out var index))
                return;

            var lastIndex = _liveIds.Count - 1;
            var lastId = _liveIds[lastIndex];
            _liveIds[index] = lastId;
            _liveIndex[lastId] = index;
            _liveIds.RemoveAt(lastIndex);

            _liveIndex.Remove(orderId);
            _liveInfo.Remove(orderId);
        }

        private bool TryPickLive(out Guid orderId, out LiveOrderInfo info)
        {
            if (_liveIds.Count == 0)
            {
                orderId = default;
                info = default!;
                return false;
            }

            orderId = _liveIds[_random.Next(_liveIds.Count)];
            info = _liveInfo[orderId];
            return true;
        }

        private OrderBookAction NextAction()
        {
            var hasLive = _liveIds.Count > 0;
            var roll = _random.NextDouble();

            if (hasLive)
            {
                if (roll < _options.CancelWeight)
                    return BuildCancel();
                roll -= _options.CancelWeight;

                if (roll < _options.UpdateWeight)
                    return BuildUpdate();
                roll -= _options.UpdateWeight;
            }

            if (roll < _options.MarketOrderWeight)
                return BuildCreateMarket();

            return BuildCreateLimit();
        }

        private CreateOrder BuildCreateLimit()
        {
            var side = _random.Next(2) == 0 ? Side.Buy : Side.Sell;
            var price = ComputePrice(side);
            var quantity = _random.Next(_options.MinQuantity, _options.MaxQuantity + 1);

            return new CreateOrder(_security, Guid.NewGuid(), Guid.NewGuid(), OrderValidity.GoodTilCanceled, side,
                quantity, price);
        }

        private CreateOrder BuildCreateMarket()
        {
            var side = _random.Next(2) == 0 ? Side.Buy : Side.Sell;
            var quantity = _random.Next(_options.MinQuantity, _options.MaxQuantity + 1);

            return new CreateOrder(_security, Guid.NewGuid(), Guid.NewGuid(), OrderValidity.GoodTilCanceled, side,
                quantity);
        }

        private CancelOrder BuildCancel()
        {
            TryPickLive(out var orderId, out var info);
            return new CancelOrder(_security, info.ClientId, orderId);
        }

        private UpdateOrder BuildUpdate()
        {
            TryPickLive(out var orderId, out var info);

            // stop orders keep a Price/TriggerPrice relationship that a blind tweak could
            // violate, so only ever adjust their quantity.
            var updateQuantityOnly = info.TriggerPrice.HasValue;
            if (updateQuantityOnly || _random.NextDouble() < 0.5)
            {
                var quantity = _random.Next(_options.MinQuantity, _options.MaxQuantity + 1);
                return new UpdateOrder(_security, info.ClientId, orderId, Quantity: quantity);
            }

            var tick = _security.TickSize;
            var direction = info.Side == Side.Buy ? -1 : 1; // move away from touch, staying passive
            var reference = info.Price ?? AlignToTick(_options.StartingPrice);
            var newPrice = reference + direction * _random.Next(1, 4) * tick;

            return new UpdateOrder(_security, info.ClientId, orderId, Price: newPrice);
        }

        private decimal ComputePrice(Side side)
        {
            var tick = _security.TickSize;
            var bestBuy = _shadowBook.GetLevels(Side.Buy, 1);
            var bestSell = _shadowBook.GetLevels(Side.Sell, 1);

            if (bestBuy.Count == 0 && bestSell.Count == 0)
                return AlignToTick(_options.StartingPrice);

            var opposite = side == Side.Buy ? bestSell : bestBuy;
            var own = side == Side.Buy ? bestBuy : bestSell;

            if (opposite.Count > 0 && _random.NextDouble() < _options.CrossProbability)
            {
                var throughTicks = _random.Next(0, 3);
                return side == Side.Buy
                    ? opposite[0].Price + throughTicks * tick
                    : opposite[0].Price - throughTicks * tick;
            }

            var reference = own.Count > 0 ? own[0].Price : opposite[0].Price;
            var offsetTicks = _random.Next(0, _options.PriceRangeTicks + 1);

            return side == Side.Buy ? reference - offsetTicks * tick : reference + offsetTicks * tick;
        }

        private decimal AlignToTick(decimal price)
        {
            var tick = _security.TickSize;
            return Math.Round(price / tick) * tick;
        }

        private readonly record struct LiveOrderInfo(Guid ClientId, Side Side, decimal? Price, decimal? TriggerPrice);
    }
}
