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
            var bookEvents =
                Book.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Sell, 100, 3);

            // act
            var events = producer.Process(Book, bookEvents);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(Now1, events[0].Time);
            Assert.IsNotNull(events[0].Bids);
            Assert.IsEmpty(events[0].Bids);
            Assert.IsNotNull(events[0].Offers);
            Assert.AreEqual(1, events[0].Offers.Count);
            Assert.AreEqual(1, events[0].Offers[0].Count);
            Assert.AreEqual(100, events[0].Offers[0].Price);
            Assert.AreEqual(3, events[0].Offers[0].Quantity);
        }

        [Test]
        public void LevelDataProducer_MultipleOrders_SamePrice()
        {
            // arrange
            var producer = new LevelDataProducer(2);

            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Sell, 100, 5);
            var bookEvents =
                Book.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Sell, 100, 3);

            // act
            var events = producer.Process(Book, bookEvents);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(Now1, events[0].Time);
            Assert.IsNotNull(events[0].Bids);
            Assert.IsEmpty(events[0].Bids);
            Assert.IsNotNull(events[0].Offers);
            Assert.AreEqual(1, events[0].Offers.Count);
            Assert.AreEqual(2, events[0].Offers[0].Count);
            Assert.AreEqual(100, events[0].Offers[0].Price);
            Assert.AreEqual(8, events[0].Offers[0].Quantity);
        }

        [Test]
        public void LevelDataProducer_MultipleOffers_DifferentPrice()
        {
            // arrange
            var producer = new LevelDataProducer(2);

            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Sell, 100, 5);
            var bookEvents =
                Book.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Sell, 110, 3);

            // act
            var events = producer.Process(Book, bookEvents);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(Now1, events[0].Time);
            Assert.IsNotNull(events[0].Bids);
            Assert.IsEmpty(events[0].Bids);
            Assert.IsNotNull(events[0].Offers);
            Assert.AreEqual(2, events[0].Offers.Count);
            Assert.AreEqual(1, events[0].Offers[0].Count);
            Assert.AreEqual(100, events[0].Offers[0].Price);
            Assert.AreEqual(5, events[0].Offers[0].Quantity);
            Assert.AreEqual(1, events[0].Offers[1].Count);
            Assert.AreEqual(110, events[0].Offers[1].Price);
            Assert.AreEqual(3, events[0].Offers[1].Quantity);
        }

        [Test]
        public void LevelDataProducer_MultipleBids_DifferentPrice()
        {
            // arrange
            var producer = new LevelDataProducer(2);

            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Buy, 100, 5);
            var bookEvents = Book.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Buy, 110, 3);

            // act
            var events = producer.Process(Book, bookEvents);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(Now1, events[0].Time);
            Assert.IsNotNull(events[0].Bids);
            Assert.AreEqual(2, events[0].Bids.Count);
            Assert.AreEqual(1, events[0].Bids[0].Count);
            Assert.AreEqual(110, events[0].Bids[0].Price);
            Assert.AreEqual(3, events[0].Bids[0].Quantity);
            Assert.AreEqual(1, events[0].Bids[1].Count);
            Assert.AreEqual(100, events[0].Bids[1].Price);
            Assert.AreEqual(5, events[0].Bids[1].Quantity);
            Assert.IsNotNull(events[0].Offers);
            Assert.IsEmpty(events[0].Offers);
        }

        [Test]
        public void LevelDataProducer_MultipleBids_OppositeSides()
        {
            // arrange
            var producer = new LevelDataProducer(2);

            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Buy, 100, 5);
            var bookEvents =
                Book.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Sell, 110, 3);

            // act
            var events = producer.Process(Book, bookEvents);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(Now1, events[0].Time);
            Assert.IsNotNull(events[0].Bids);
            Assert.AreEqual(1, events[0].Bids.Count);
            Assert.AreEqual(1, events[0].Bids[0].Count);
            Assert.AreEqual(100, events[0].Bids[0].Price);
            Assert.AreEqual(5, events[0].Bids[0].Quantity);
            Assert.IsNotNull(events[0].Offers);
            Assert.AreEqual(1, events[0].Offers.Count);
            Assert.AreEqual(1, events[0].Offers[0].Count);
            Assert.AreEqual(110, events[0].Offers[0].Price);
            Assert.AreEqual(3, events[0].Offers[0].Quantity);
        }

        [Test]
        public void LevelDataProducer_MultipleBids_LimitedToMaxLevels()
        {
            // arrange
            var producer = new LevelDataProducer(2);

            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Buy, 110, 3);
            Book.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Buy, 120, 4);
            var bookEvents = Book.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Buy, 130, 5);

            // act
            var events = producer.Process(Book, bookEvents);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(Now1, events[0].Time);
            Assert.IsNotNull(events[0].Bids);
            Assert.AreEqual(2, events[0].Bids.Count);
            Assert.AreEqual(1, events[0].Bids[0].Count);
            Assert.AreEqual(130, events[0].Bids[0].Price);
            Assert.AreEqual(5, events[0].Bids[0].Quantity);
            Assert.AreEqual(1, events[0].Bids[1].Count);
            Assert.AreEqual(120, events[0].Bids[1].Price);
            Assert.AreEqual(4, events[0].Bids[1].Quantity);
            Assert.IsNotNull(events[0].Offers);
            Assert.IsEmpty(events[0].Offers);
        }

        [Test]
        public void LevelDataProducer_Trade_UpdatesCorrectly()
        {
            // arrange
            var producer = new LevelDataProducer(2);

            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Buy, 110, 3);
            Book.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Buy, 120, 4);
            var bookEvents =
                Book.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Sell, 100, 5);

            // act
            var events = producer.Process(Book, bookEvents);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(Now1, events[0].Time);
            Assert.IsNotNull(events[0].Bids);
            Assert.AreEqual(1, events[0].Bids.Count);
            Assert.AreEqual(1, events[0].Bids[0].Count);
            Assert.AreEqual(110, events[0].Bids[0].Price);
            Assert.AreEqual(2, events[0].Bids[0].Quantity);
            Assert.IsNotNull(events[0].Offers);
            Assert.IsEmpty(events[0].Offers);
        }
    }
}