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
            var events = book.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Sell, 100, 3);
            
            TradedMarketDataArgs traded = null;
            producer.Traded += (_, args) => traded = args;
            
            // act
            producer.Process(book, events);

            // assert
            Assert.IsNotNull(traded);
            Assert.AreEqual(now, traded.Time);
            Assert.AreEqual(100, traded.Price);
            Assert.AreEqual(3, traded.Quantity);
        }
    }
}