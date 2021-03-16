using System;
using System.Collections.Generic;
using Circus.DataProducers;
using Circus.OrderBook;
using Circus.TimeProviders;

namespace Circus.Examples
{
    public class Example
    {
        public static void Run()
        {
            var time = new UtcTimeProvider();

            var producer = new TradeDataProducer();

            var sec1 = new Security("GCZ6", SecurityType.Future, 10, 10);
            IOrderBook book1 = new InMemoryOrderBook(sec1, time);

            Print(producer.Process(book1,
                book1.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Buy, 100, 3)));

            Print(producer.Process(book1,
                book1.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Sell, 100, 5)));
        }

        private static void Print(IEnumerable<TradedDataEvent> events)
        {
            foreach (var @event in events)
            {
                Console.WriteLine(@event);
            }
        }
    }
}