using System;
using Circus.DataProducers;
using Circus.OrderBook;
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
            book.OrderBookEvent += (_, args) => producer.Process(book, args.Events);
            book.SetStatus(OrderBookStatus.Open);
            book.CreateLimitOrder(Guid.NewGuid(), TimeInForce.Day, Side.Buy, 100, 3);

            TradedMarketDataArgs traded = null;
            producer.Traded += (_, args) => traded = args;
            
            // act
            book.CreateLimitOrder(Guid.NewGuid(), TimeInForce.Day, Side.Sell, 100, 3);

            // assert
            Assert.IsNotNull(traded);
            Assert.AreEqual(now, traded.Time);
            Assert.AreEqual(100, traded.Price);
            Assert.AreEqual(3, traded.Quantity);
        }
    }
}