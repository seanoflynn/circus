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

            var feed1 = new BookFeed();
            var feed2 = new BookFeed();

            // book1 opens on an auction: orders accumulate in pre-open with the indicative quote
            // tracking them, and the print happens on the way out - the same orders as book2, which
            // opens first and trades them continuously.
            feed1.Publish(book1, book1.UpdateStatus(OrderBookStatus.PreOpen));
            feed1.Publish(book1,
                book1.CreateLimitOrder("Buyer", "Order1", new OrderValidity.Day(), Side.Buy, 3, 100));
            feed1.Publish(book1,
                book1.CreateLimitOrder("Seller", "Order2", new OrderValidity.Day(), Side.Sell, 5, 100));
            feed1.Publish(book1, book1.UpdateStatus(OrderBookStatus.Open));

            feed2.Publish(book2, book2.UpdateStatus(OrderBookStatus.Open));
            feed2.Publish(book2,
                book2.CreateLimitOrder("Buyer", "Order1", new OrderValidity.Day(), Side.Buy, 3, 100));
            feed2.Publish(book2,
                book2.CreateLimitOrder("Seller", "Order2", new OrderValidity.Day(), Side.Sell, 5, 100));
        }

        // One set of producers per book. The level and status producers each build their own view
        // of a single book out of its event stream alone, so a set cannot be shared across two -
        // and there is no way to resync one that missed an event, which is why they are created
        // before the book they follow processes anything.
        private sealed class BookFeed
        {
            private readonly TradeDataProducer _trades = new();
            private readonly LevelDataProducer _bbo = new(1);
            private readonly LevelDataProducer _top10 = new(10);
            private readonly FullBookDataProducer _fullBook = new();
            private readonly IndicativePriceDataProducer _indicative = new();
            private readonly SecurityStatusDataProducer _status = new();

            public void Publish(IOrderBook book, IReadOnlyList<OrderBookEvent> events)
            {
                Print(_trades.Process(book, events));
                Print(_bbo.Process(book, events));
                Print(_top10.Process(book, events));
                Print(_fullBook.Process(book, events));
                Print(_indicative.Process(book, events));
                Print(_status.Process(book, events));
            }
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

        private static void Print(IEnumerable<IndicativePriceDataEvent> events)
        {
            foreach (var @event in events)
            {
                Console.WriteLine(@event);
            }
        }

        private static void Print(IEnumerable<OrderBookDeltaEvent> events)
        {
            foreach (var @event in events)
            {
                Console.WriteLine(@event);
            }
        }

        private static void Print(IEnumerable<SecurityStatusDataEvent> events)
        {
            foreach (var @event in events)
            {
                Console.WriteLine(@event);
            }
        }
    }
}
