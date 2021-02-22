using System;
using System.Collections.Generic;
using Circus.OrderBook;

namespace Circus.MarketData
{
    public interface IMarketDataProducer
    {
        void Process(OrderBook.OrderBook book, IEnumerable<OrderBookEvent> events);
    }

    public record TradedMarketDataArgs(DateTime Time, decimal Price, int Quantity);

    public record LevelsUpdatedMarketDataArgs(IList<Level> Bids, IList<Level> Offers);
}