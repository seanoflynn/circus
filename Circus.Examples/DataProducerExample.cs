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

            // LevelDataProducer maintains its own per-book state, so each book needs its own
            // instances (one per data tier) rather than sharing a single producer across books.
            var tradeDataProducer1 = new TradeDataProducer();
            var bboDataProducer1 = new LevelDataProducer(1);
            var top10DataProducer1 = new LevelDataProducer(10);
            var fullBookDataProducer1 = new OrderBookDeltaDataProducer();

            var tradeDataProducer2 = new TradeDataProducer();
            var bboDataProducer2 = new LevelDataProducer(1);
            var top10DataProducer2 = new LevelDataProducer(10);
            var fullBookDataProducer2 = new OrderBookDeltaDataProducer();

            void Publish(IOrderBook book, IReadOnlyList<OrderBookEvent> events, TradeDataProducer tradeDataProducer,
                LevelDataProducer bboDataProducer, LevelDataProducer top10DataProducer,
                OrderBookDeltaDataProducer fullBookDataProducer)
            {
                Print(tradeDataProducer.Process(book, events));
                Print(bboDataProducer.Process(book, events));
                Print(top10DataProducer.Process(book, events));
                Print(fullBookDataProducer.Process(book, events));
            }

            Publish(book1, book1.UpdateStatus(OrderBookStatus.Open), tradeDataProducer1, bboDataProducer1,
                top10DataProducer1, fullBookDataProducer1);
            Publish(book1, book1.CreateLimitOrder("Buyer", "Order1", new OrderValidity.Day(), Side.Buy, 3, 100),
                tradeDataProducer1, bboDataProducer1, top10DataProducer1, fullBookDataProducer1);
            Publish(book1, book1.CreateLimitOrder("Seller", "Order2", new OrderValidity.Day(), Side.Sell, 5, 100),
                tradeDataProducer1, bboDataProducer1, top10DataProducer1, fullBookDataProducer1);

            Publish(book2, book2.UpdateStatus(OrderBookStatus.Open), tradeDataProducer2, bboDataProducer2,
                top10DataProducer2, fullBookDataProducer2);
            Publish(book2, book2.CreateLimitOrder("Buyer", "Order1", new OrderValidity.Day(), Side.Buy, 3, 100),
                tradeDataProducer2, bboDataProducer2, top10DataProducer2, fullBookDataProducer2);
            Publish(book2, book2.CreateLimitOrder("Seller", "Order2", new OrderValidity.Day(), Side.Sell, 5, 100),
                tradeDataProducer2, bboDataProducer2, top10DataProducer2, fullBookDataProducer2);
        }

        private static void Print(IEnumerable<TradedDataEvent> events)
        {
            foreach (var @event in events)
            {
                Console.WriteLine(@event);
            }
        }

        private static void Print(IEnumerable<LevelsDataEvent> events)
        {
            foreach (var @event in events)
            {
                Console.WriteLine(
                    $"LevelsDataEvent {{ Bids = [{string.Join(", ", @event.Bids)}], Offers = [{string.Join(", ", @event.Offers)}] }}");
            }
        }

        private static void Print(IEnumerable<OrderBookDeltaEvent> events)
        {
            foreach (var @event in events)
            {
                Console.WriteLine(@event);
            }
        }
    }
}
