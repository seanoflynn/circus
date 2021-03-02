using System;
using System.Collections.Generic;
using Circus.OrderBook;

namespace Circus.DataProducers
{
    // TODO: merge into 1 event?

    public class TradeDataProducer : IDataProducer
    {
        public event EventHandler<TradedMarketDataArgs>? Traded;

        public void Process(IOrderBook book, IEnumerable<OrderBookEvent> events)
        {
            foreach (var ev in events)
            {
                if (ev is OrderMatchedEvent matched)
                {
                    Traded?.Invoke(this,
                        new TradedMarketDataArgs(matched.Time, matched.Price, matched.Quantity));
                }
            }
        }
    }

    public record TradedMarketDataArgs(DateTime Time, decimal Price, int Quantity);
}