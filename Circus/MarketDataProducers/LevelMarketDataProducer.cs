using System;
using System.Collections.Generic;
using Circus.OrderBook;

namespace Circus.MarketDataProducers
{
    public class LevelMarketDataProducer : IMarketDataProducer
    {
        private readonly int _maxLevels;
        public event EventHandler<TradedMarketDataArgs> Traded;
        public event EventHandler<LevelsUpdatedMarketDataArgs> LevelsUpdated;

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
    }
}