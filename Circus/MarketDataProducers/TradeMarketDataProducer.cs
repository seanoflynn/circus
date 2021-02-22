using System;
using System.Collections.Generic;
using Circus.OrderBook;

namespace Circus.MarketDataProducers
{
    public class TradeMarketDataProducer : IMarketDataProducer
    {
        public event EventHandler<TradedMarketDataArgs> Traded;

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
        }
    }
}