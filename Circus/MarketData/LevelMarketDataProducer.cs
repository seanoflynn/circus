using System;
using System.Collections.Generic;
using Circus.OrderBook;

namespace Circus.MarketData
{
    public class LevelMarketDataProducer : IMarketDataProducer
    {
        private readonly int _maxLevels;
        public event EventHandler<TradedMarketDataArgs> Traded;
        public event EventHandler<LevelsUpdatedMarketDataArgs> LevelsUpdated;

        // private readonly Dictionary<Guid, decimal> _priceByOrderId = new();
        // private readonly Dictionary<Guid, int> _quantityByOrderId = new();
        //
        // private readonly SortedDictionary<decimal, Level> _bids = new(new DescendingComparer());
        // private readonly SortedDictionary<decimal, Level> _offers = new();

        public LevelMarketDataProducer(int maxLevels)
        {
            _maxLevels = maxLevels;
        }

        public void Process(OrderBook.OrderBook book, IEnumerable<OrderBookEvent> events)
        {
            foreach (var ev in events)
            {
                if (ev is OrderMatchedEvent matched)
                {
                    Traded?.Invoke(this,
                        new TradedMarketDataArgs(matched.Fill.Time, matched.Fill.Price, matched.Fill.Quantity));
                }
            }

            var bids = book.GetLevels(Side.Buy, _maxLevels);
            var offers = book.GetLevels(Side.Sell, _maxLevels);

            LevelsUpdated?.Invoke(this, new LevelsUpdatedMarketDataArgs(bids, offers));
        }

        // private void ProcessCreated(OrderCreatedEvent e)
        // {
        //     var order = e.Order;
        //     var levels = order.Side == Side.Buy ? _bids : _offers;
        //     levels.Add(order.Price, order.Quantity);
        //
        //     _priceByOrderId.Add(order.Id, order.Price);
        //     _quantityByOrderId.Add(order.Id, order.Quantity);
        // }
        //
        // private void ProcessUpdated(OrderUpdatedEvent e)
        // {
        //     var order = e.Order;
        //     var levels = order.Side == Side.Buy ? _bids : _offers;
        //
        //     var oldPrice = _priceByOrderId[order.Id];
        //     var oldQuantity = _quantityByOrderId[order.Id];
        //     if (oldPrice != order.Price)
        //     {
        //         levels.Remove(oldPrice, oldQuantity);
        //         levels.Add(order.Price, order.Quantity);
        //     }
        //     else
        //     {
        //         levels.Update(order.Price, oldQuantity, order.Quantity);
        //     }
        //
        //     _priceByOrderId[order.Id] = order.Price;
        //     _quantityByOrderId[order.Id] = order.Quantity;
        // }
        //
        // private void ProcessFilled(OrderMatchedEvent e)
        // {
        //     var (fill, resting, aggressor) = e;
        //     foreach (var order in new[] {aggressor, resting})
        //     {
        //         var levels = order.Side == Side.Buy ? _bids : _offers;
        //         if (order.Status == OrderStatus.Filled)
        //         {
        //             levels.Remove(order.Price, fill.Quantity);
        //
        //             _priceByOrderId.Remove(order.Id);
        //             _quantityByOrderId.Remove(order.Id);
        //         }
        //         else
        //         {
        //             var oldQuantity = _quantityByOrderId[order.Id];
        //             levels.Update(order.Price, oldQuantity, order.Quantity);
        //             _quantityByOrderId[order.Id] = order.Quantity;
        //         }
        //     }
        // }
        //
        // private void ProcessCancelled(OrderCancelledEvent e)
        // {
        //     var order = e.Order;
        //     var levels = order.Side == Side.Buy ? _bids : _offers;
        //     levels.Remove(order.Price, order.Quantity - order.FilledQuantity);
        //
        //     _priceByOrderId.Remove(order.Id);
        //     _quantityByOrderId.Remove(order.Id);
        // }
        //
        // private void ProcessExpired(OrderExpiredEvent e)
        // {
        //     var order = e.Order;
        //     var levels = order.Side == Side.Buy ? _bids : _offers;
        //     levels.Remove(order.Price, order.Quantity - order.FilledQuantity);
        //
        //     _priceByOrderId.Remove(order.Id);
        //     _quantityByOrderId.Remove(order.Id);
        // }
    }

    // internal static class SortedDictionaryExtensions
    // {
    //     internal static void Add(this SortedDictionary<decimal, Level> levels, decimal price, int quantity)
    //     {
    //         if (!levels.TryGetValue(price, out var level))
    //         {
    //             level = new Level();
    //             levels.Add(price, level);
    //         }
    //
    //         level.Quantity += quantity;
    //         level.Count++;
    //     }
    //
    //     internal static void Remove(this SortedDictionary<decimal, Level> levels, decimal price, int quantity)
    //     {
    //         var existing = levels[price];
    //         existing.Quantity -= quantity;
    //         existing.Count--;
    //
    //         if (existing.Count < 1)
    //         {
    //             levels.Remove(price);
    //         }
    //     }
    //
    //     internal static void Update(this SortedDictionary<decimal, Level> levels, decimal price, int oldQuantity,
    //         int newQuantity)
    //     {
    //         var existing = levels[price];
    //         existing.Quantity += (oldQuantity - newQuantity);
    //     }
    // }
}