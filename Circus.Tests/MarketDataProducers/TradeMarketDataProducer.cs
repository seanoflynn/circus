using System;
using System.Linq;
using Circus.MarketDataProducers;
using Circus.OrderBook;
using NUnit.Framework;

namespace Circus.Tests.MarketDataProducers
{
	public class TradeMarketDataProducerTests
	{
		[Test]
		public void CreateMarketOrder_MarketClosed_Rejected()
		{
			// arrange
			var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
			var now = new DateTime(2000, 1, 1, 12, 0, 0);
			var timeProvider = new TestTimeProvider(now);
			var book = new Circus.OrderBook.OrderBook(sec, timeProvider);
			book.SetStatus(OrderBookStatus.Open);
			book.CreateLimitOrder(Guid.NewGuid(), TimeInForce.Day, Side.Buy, 100, 3);
			var events = book.CreateLimitOrder(Guid.NewGuid(), TimeInForce.Day, Side.Sell, 100, 3);

			var producer = new TradeMarketDataProducer();
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
