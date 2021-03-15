using System;
using Circus.DataProducers;
using Circus.OrderBook;
using Circus.TimeProviders;
using NUnit.Framework;

namespace Circus.Tests.DataProducers
{
    public class LevelDataProducerTests
    {
        private static readonly Security Sec = new("GCZ6", SecurityType.Future, 10, 10);

        private static readonly DateTime Now1 = new(2000, 1, 1, 12, 0, 0);

        private static TestTimeProvider TimeProvider;
        private static IOrderBook Book;

        private static readonly Guid ClientId1 = Guid.NewGuid();
        private static readonly Guid ClientId2 = Guid.NewGuid();
        private static readonly Guid ClientId3 = Guid.NewGuid();

        private static readonly Guid OrderId1 = Guid.NewGuid();
        private static readonly Guid OrderId2 = Guid.NewGuid();
        private static readonly Guid OrderId3 = Guid.NewGuid();

        [SetUp]
        public void SetUp()
        {
            TimeProvider = new TestTimeProvider(Now1);
            Book = new InMemoryOrderBook(Sec, TimeProvider);
        }
        
        [Test]
        public void LevelDataProducer_SingleOrder()
        {
            // arrange
            var producer = new LevelDataProducer(2);

            Book.UpdateStatus(OrderBookStatus.Open);
            var events = Book.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Sell, 100, 3);
            
            LevelsUpdatedMarketDataArgs updatedArgs = null;
            producer.LevelsUpdated += (_, args) => updatedArgs = args;
            
            // act
            producer.Process(Book, events);

            // assert
            Assert.IsNotNull(updatedArgs);
            Assert.AreEqual(Now1, updatedArgs.Time);
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
            var producer = new LevelDataProducer(2);
            LevelsUpdatedMarketDataArgs updatedArgs = null;
            producer.LevelsUpdated += (_, args) => updatedArgs = args;

            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Sell, 100, 5);
            var events = Book.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Sell, 100, 3);
            
            // act
            producer.Process(Book, events);
            
            // assert
            Assert.IsNotNull(updatedArgs);
            Assert.AreEqual(Now1, updatedArgs.Time);
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
            var producer = new LevelDataProducer(2);
            LevelsUpdatedMarketDataArgs updatedArgs = null;
            producer.LevelsUpdated += (_, args) => updatedArgs = args;
            
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Sell, 100, 5);
            var events = Book.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Sell, 110, 3);

            // act
            producer.Process(Book, events);

            // assert
            Assert.IsNotNull(updatedArgs);
            Assert.AreEqual(Now1, updatedArgs.Time);
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
            var producer = new LevelDataProducer(2);
            LevelsUpdatedMarketDataArgs updatedArgs = null;
            producer.LevelsUpdated += (_, args) => updatedArgs = args;
            
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Buy, 100, 5);
            var events = Book.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Buy, 110, 3);
            
            // act
            producer.Process(Book, events);
            
            // assert
            Assert.IsNotNull(updatedArgs);
            Assert.AreEqual(Now1, updatedArgs.Time);
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
            var producer = new LevelDataProducer(2);
            LevelsUpdatedMarketDataArgs updatedArgs = null;
            producer.LevelsUpdated += (_, args) => updatedArgs = args;
            
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Buy, 100, 5);
            var events = Book.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Sell, 110, 3);
            
            // act
            producer.Process(Book, events);

            // assert
            Assert.IsNotNull(updatedArgs);
            Assert.AreEqual(Now1, updatedArgs.Time);
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
            var producer = new LevelDataProducer(2);
            LevelsUpdatedMarketDataArgs updatedArgs = null;
            producer.LevelsUpdated += (_, args) => updatedArgs = args;
            
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Buy, 110, 3);
            Book.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Buy, 120, 4);
            var events = Book.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Buy, 130, 5);
            
            // act
            producer.Process(Book, events);

            // assert
            Assert.IsNotNull(updatedArgs);
            Assert.AreEqual(Now1, updatedArgs.Time);
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
            var producer = new LevelDataProducer(2);
            LevelsUpdatedMarketDataArgs updatedArgs = null;
            producer.LevelsUpdated += (_, args) => updatedArgs = args;

            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Buy, 110, 3);
            Book.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Buy, 120, 4);
            var events = Book.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Sell, 100, 5);
            
            // act
            producer.Process(Book, events);

            // assert
            Assert.IsNotNull(updatedArgs);
            Assert.AreEqual(Now1, updatedArgs.Time);
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