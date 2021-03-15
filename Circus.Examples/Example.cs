using System;
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

            var tradeDataProducer = new TradeDataProducer();
            tradeDataProducer.Traded += (_, args) => Console.WriteLine(args);

            var sec1 = new Security("GCZ6", SecurityType.Future, 10, 10);
            IOrderBook book1 = new InMemoryOrderBook(sec1, time);
            book1.OrderBookEvent += (_, args) => tradeDataProducer.Process(book1, args.Events);

            book1.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Buy, 100, 3);
            book1.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Sell, 100, 5);
        }
    }
}