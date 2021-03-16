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
            book.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Buy, 100, 3);
            var bookEvents =
                book.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Sell, 100, 3);

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