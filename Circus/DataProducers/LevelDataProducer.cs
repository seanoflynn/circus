using System;
using System.Collections.Generic;
using System.Linq;
using Circus.OrderBook;

namespace Circus.DataProducers
{
    // TODO: only send changes in book?

    public class LevelDataProducer : IDataProducer<LevelsDataEvent>
    {
        private readonly int _maxLevels;

        public LevelDataProducer(int maxLevels)
        {
            _maxLevels = maxLevels;
        }

        public IList<LevelsDataEvent> Process(IOrderBook book, IList<OrderBookEvent> events)
        {
            var bids = book.GetLevels(Side.Buy, _maxLevels);
            var offers = book.GetLevels(Side.Sell, _maxLevels);

            return new[] {new LevelsDataEvent(events.First().Time, bids, offers)};
        }
    }

    public record LevelsDataEvent(DateTime Time, IList<Level> Bids, IList<Level> Offers);
}