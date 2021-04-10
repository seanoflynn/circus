using System;
using Circus.DataProducers;
using Circus.OrderBook;
using Circus.TimeProviders;
using NUnit.Framework;

namespace Circus.Tests.DataProducers
{
    public class TradeDataProducerTests
    {
        [Test]
        public void TradeDataProducer_Traded_FiresEvent()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now);
            var producer = new TradeDataProducer();

            var book = new InMemoryOrderBook(sec, timeProvider);
            book.UpdateStatus(OrderBookStatus.Open);
            book.CreateOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Buy, 3, 100);
            var bookEvents =
                book.CreateOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Sell, 3, 100);

            // act
            var events = producer.Process(book, bookEvents);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(now, events[0].Time);
            Assert.AreEqual(100, events[0].Price);
            Assert.AreEqual(3, events[0].Quantity);
        }
    }
}