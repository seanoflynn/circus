using System;
using Circus.DataProducers;
using Circus.OrderBook;
using NUnit.Framework;

namespace Circus.Tests.DataProducers
{
    public class LevelDataProducerTests
    {
        [Test]
        public void LevelDataProducer_SingleOrder()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now);
            var producer = new LevelDataProducer(2);

            var book = new InMemoryOrderBook(sec, timeProvider);
            book.OrderBookEvent += (_, args) => producer.Process(book, args.Events);
            book.SetStatus(OrderBookStatus.Open);

            LevelsUpdatedMarketDataArgs updatedArgs = null;
            producer.LevelsUpdated += (_, args) => updatedArgs = args;
            
            // act
            book.CreateLimitOrder(Guid.NewGuid(), TimeInForce.Day, Side.Sell, 100, 3);

            // assert
            Assert.IsNotNull(updatedArgs);
            Assert.AreEqual(now, updatedArgs.Time);
            Assert.IsNotNull(updatedArgs.Bids);
            Assert.IsEmpty(updatedArgs.Bids);
            Assert.IsNotNull(updatedArgs.Offers);
            Assert.AreEqual(1, updatedArgs.Offers.Count);
            Assert.AreEqual(1, updatedArgs.Offers[0].Count);
            Assert.AreEqual(100, updatedArgs.Offers[0].Price);
            Assert.AreEqual(3, updatedArgs.Offers[0].Quantity);
        }
        
        [Test]
        public void LevelDataProducer_MultipleOrders_SamePrice()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now);
            var producer = new LevelDataProducer(2);

            var book = new InMemoryOrderBook(sec, timeProvider);
            book.OrderBookEvent += (_, args) => producer.Process(book, args.Events);
            book.SetStatus(OrderBookStatus.Open);
            book.CreateLimitOrder(Guid.NewGuid(), TimeInForce.Day, Side.Sell, 100, 5);
            
            LevelsUpdatedMarketDataArgs updatedArgs = null;
            producer.LevelsUpdated += (_, args) => updatedArgs = args;
            
            // act
            book.CreateLimitOrder(Guid.NewGuid(), TimeInForce.Day, Side.Sell, 100, 3);

            // assert
            Assert.IsNotNull(updatedArgs);
            Assert.AreEqual(now, updatedArgs.Time);
            Assert.IsNotNull(updatedArgs.Bids);
            Assert.IsEmpty(updatedArgs.Bids);
            Assert.IsNotNull(updatedArgs.Offers);
            Assert.AreEqual(1, updatedArgs.Offers.Count);
            Assert.AreEqual(2, updatedArgs.Offers[0].Count);
            Assert.AreEqual(100, updatedArgs.Offers[0].Price);
            Assert.AreEqual(8, updatedArgs.Offers[0].Quantity);
        }
        
        [Test]
        public void LevelDataProducer_MultipleOffers_DifferentPrice()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now);
            var producer = new LevelDataProducer(2);

            var book = new InMemoryOrderBook(sec, timeProvider);
            book.OrderBookEvent += (_, args) => producer.Process(book, args.Events);
            book.SetStatus(OrderBookStatus.Open);
            book.CreateLimitOrder(Guid.NewGuid(), TimeInForce.Day, Side.Sell, 100, 5);
            
            LevelsUpdatedMarketDataArgs updatedArgs = null;
            producer.LevelsUpdated += (_, args) => updatedArgs = args;
            
            // act
            book.CreateLimitOrder(Guid.NewGuid(), TimeInForce.Day, Side.Sell, 110, 3);

            // assert
            Assert.IsNotNull(updatedArgs);
            Assert.AreEqual(now, updatedArgs.Time);
            Assert.IsNotNull(updatedArgs.Bids);
            Assert.IsEmpty(updatedArgs.Bids);
            Assert.IsNotNull(updatedArgs.Offers);
            Assert.AreEqual(2, updatedArgs.Offers.Count);
            Assert.AreEqual(1, updatedArgs.Offers[0].Count);
            Assert.AreEqual(100, updatedArgs.Offers[0].Price);
            Assert.AreEqual(5, updatedArgs.Offers[0].Quantity);
            Assert.AreEqual(1, updatedArgs.Offers[1].Count);
            Assert.AreEqual(110, updatedArgs.Offers[1].Price);
            Assert.AreEqual(3, updatedArgs.Offers[1].Quantity);
        }
        
        [Test]
        public void LevelDataProducer_MultipleBids_DifferentPrice()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now);
            var producer = new LevelDataProducer(2);

            var book = new InMemoryOrderBook(sec, timeProvider);
            book.OrderBookEvent += (_, args) => producer.Process(book, args.Events);
            book.SetStatus(OrderBookStatus.Open);
            book.CreateLimitOrder(Guid.NewGuid(), TimeInForce.Day, Side.Buy, 100, 5);
            
            LevelsUpdatedMarketDataArgs updatedArgs = null;
            producer.LevelsUpdated += (_, args) => updatedArgs = args;
            
            // act
            book.CreateLimitOrder(Guid.NewGuid(), TimeInForce.Day, Side.Buy, 110, 3);

            // assert
            Assert.IsNotNull(updatedArgs);
            Assert.AreEqual(now, updatedArgs.Time);
            Assert.IsNotNull(updatedArgs.Bids);
            Assert.AreEqual(2, updatedArgs.Bids.Count);
            Assert.AreEqual(1, updatedArgs.Bids[0].Count);
            Assert.AreEqual(110, updatedArgs.Bids[0].Price);
            Assert.AreEqual(3, updatedArgs.Bids[0].Quantity);
            Assert.AreEqual(1, updatedArgs.Bids[1].Count);
            Assert.AreEqual(100, updatedArgs.Bids[1].Price);
            Assert.AreEqual(5, updatedArgs.Bids[1].Quantity);
            Assert.IsNotNull(updatedArgs.Offers);
            Assert.IsEmpty(updatedArgs.Offers);
        }
        
        [Test]
        public void LevelDataProducer_MultipleBids_OppositeSides()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now);
            var producer = new LevelDataProducer(2);

            var book = new InMemoryOrderBook(sec, timeProvider);
            book.OrderBookEvent += (_, args) => producer.Process(book, args.Events);
            book.SetStatus(OrderBookStatus.Open);
            book.CreateLimitOrder(Guid.NewGuid(), TimeInForce.Day, Side.Buy, 100, 5);
            
            LevelsUpdatedMarketDataArgs updatedArgs = null;
            producer.LevelsUpdated += (_, args) => updatedArgs = args;
            
            // act
            book.CreateLimitOrder(Guid.NewGuid(), TimeInForce.Day, Side.Sell, 110, 3);

            // assert
            Assert.IsNotNull(updatedArgs);
            Assert.AreEqual(now, updatedArgs.Time);
            Assert.IsNotNull(updatedArgs.Bids);
            Assert.AreEqual(1, updatedArgs.Bids.Count);
            Assert.AreEqual(1, updatedArgs.Bids[0].Count);
            Assert.AreEqual(100, updatedArgs.Bids[0].Price);
            Assert.AreEqual(5, updatedArgs.Bids[0].Quantity);
            Assert.IsNotNull(updatedArgs.Offers);
            Assert.AreEqual(1, updatedArgs.Offers.Count);
            Assert.AreEqual(1, updatedArgs.Offers[0].Count);
            Assert.AreEqual(110, updatedArgs.Offers[0].Price);
            Assert.AreEqual(3, updatedArgs.Offers[0].Quantity);
        }
        
        [Test]
        public void LevelDataProducer_MultipleBids_LimitedToMaxLevels()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now);
            var producer = new LevelDataProducer(2);

            var book = new InMemoryOrderBook(sec, timeProvider);
            book.OrderBookEvent += (_, args) => producer.Process(book, args.Events);
            book.SetStatus(OrderBookStatus.Open);
            book.CreateLimitOrder(Guid.NewGuid(), TimeInForce.Day, Side.Buy, 110, 3);
            book.CreateLimitOrder(Guid.NewGuid(), TimeInForce.Day, Side.Buy, 120, 4);
            
            LevelsUpdatedMarketDataArgs updatedArgs = null;
            producer.LevelsUpdated += (_, args) => updatedArgs = args;
            
            // act
            book.CreateLimitOrder(Guid.NewGuid(), TimeInForce.Day, Side.Buy, 130, 5);

            // assert
            Assert.IsNotNull(updatedArgs);
            Assert.AreEqual(now, updatedArgs.Time);
            Assert.IsNotNull(updatedArgs.Bids);
            Assert.AreEqual(2, updatedArgs.Bids.Count);
            Assert.AreEqual(1, updatedArgs.Bids[0].Count);
            Assert.AreEqual(130, updatedArgs.Bids[0].Price);
            Assert.AreEqual(5, updatedArgs.Bids[0].Quantity);
            Assert.AreEqual(1, updatedArgs.Bids[1].Count);
            Assert.AreEqual(120, updatedArgs.Bids[1].Price);
            Assert.AreEqual(4, updatedArgs.Bids[1].Quantity);
            Assert.IsNotNull(updatedArgs.Offers);
            Assert.IsEmpty(updatedArgs.Offers);
        }
        
        [Test]
        public void LevelDataProducer_Trade_UpdatesCorrectly()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now);
            var producer = new LevelDataProducer(2);

            var book = new InMemoryOrderBook(sec, timeProvider);
            book.OrderBookEvent += (_, args) => producer.Process(book, args.Events);
            book.SetStatus(OrderBookStatus.Open);
            book.CreateLimitOrder(Guid.NewGuid(), TimeInForce.Day, Side.Buy, 110, 3);
            book.CreateLimitOrder(Guid.NewGuid(), TimeInForce.Day, Side.Buy, 120, 4);
            
            LevelsUpdatedMarketDataArgs updatedArgs = null;
            producer.LevelsUpdated += (_, args) => updatedArgs = args;
            
            // act
            book.CreateLimitOrder(Guid.NewGuid(), TimeInForce.Day, Side.Sell, 100, 5);

            // assert
            Assert.IsNotNull(updatedArgs);
            Assert.AreEqual(now, updatedArgs.Time);
            Assert.IsNotNull(updatedArgs.Bids);
            Assert.AreEqual(1, updatedArgs.Bids.Count);
            Assert.AreEqual(1, updatedArgs.Bids[0].Count);
            Assert.AreEqual(110, updatedArgs.Bids[0].Price);
            Assert.AreEqual(2, updatedArgs.Bids[0].Quantity);
            Assert.IsNotNull(updatedArgs.Offers);
            Assert.IsEmpty(updatedArgs.Offers);
        }
    }
}