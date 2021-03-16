using System;
using System.Collections.Generic;
using Circus.OrderBook;

namespace Circus.DataProducers
{
    public class TradeDataProducer : IDataProducer<TradedDataEvent>
    {
        public IList<TradedDataEvent> Process(IOrderBook book, IList<OrderBookEvent> events)
        {
            var output = new List<TradedDataEvent>();

            foreach (var ev in events)
            {
                if (ev is OrderMatched matched)
                {
                    output.Add(new TradedDataEvent(matched.Time, matched.Price, matched.Quantity));
                }
            }

            return output;
        }
    }

    public record TradedDataEvent(DateTime Time, decimal Price, int Quantity);
}