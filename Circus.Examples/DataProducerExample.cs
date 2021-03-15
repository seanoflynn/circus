using System;
using System.Collections.Generic;
using Circus.DataProducers;
using Circus.OrderBook;
using Circus.TimeProviders;

namespace Circus.Examples
{
    public class MarketDataProducerExample
    {
        public static void Run()
        {
            var time = new UtcTimeProvider();
            
            var sec1 = new Security("GCZ6", SecurityType.Future, 10, 10);
            var sec2 = new Security("SIZ6", SecurityType.Future, 10, 10);
            
            IOrderBook book1 = new InMemoryOrderBook(sec1, time);
            IOrderBook book2 = new InMemoryOrderBook(sec2, time);

            var tradeDataProducer = new TradeDataProducer();
            tradeDataProducer.Traded += (_, args) => Console.WriteLine(args);
            var levelDataProducer = new LevelDataProducer(5);
            levelDataProducer.LevelsUpdated += (_, args) => Console.WriteLine(args);

            void Publish(IOrderBook book, IList<OrderBookEvent> events)
            {
                tradeDataProducer.Process(book, events);
                levelDataProducer.Process(book, events);
            }
            
            Publish(book1, book1.UpdateStatus(OrderBookStatus.Open));
            Publish(book1, book1.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Buy, 100, 3));
            Publish(book1,
                book1.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Sell, 100, 5));
            
            Publish(book2, book2.UpdateStatus(OrderBookStatus.Open));
            Publish(book2, book2.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Buy, 100, 3));
            Publish(book2,
                book2.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Sell, 100, 5));
        }
    }
}