using System;
using System.Collections.Generic;
using System.Linq;
using Circus.OrderBook;

namespace Circus.DataProducers
{
    // TODO: only send changes in book?

    public class LevelDataProducer : IDataProducer
    {
        public event EventHandler<LevelsUpdatedMarketDataArgs>? LevelsUpdated;
        private readonly int _maxLevels;

        public LevelDataProducer(int maxLevels)
        {
            _maxLevels = maxLevels;
        }

        public void Process(IOrderBook book, IEnumerable<OrderBookEvent> events)
        {
            var bids = book.GetLevels(Side.Buy, _maxLevels);
            var offers = book.GetLevels(Side.Sell, _maxLevels);

            LevelsUpdated?.Invoke(this, new LevelsUpdatedMarketDataArgs(events.First().Time, bids, offers));
        }
    }

    public record LevelsUpdatedMarketDataArgs(DateTime Time, IList<Level> Bids, IList<Level> Offers);
}