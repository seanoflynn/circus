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
                Book.CreateOrder("Company1", "Order1", OrderValidity.Day, Side.Sell, 3, 100);

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
            Book.CreateOrder("Company2", "Order2", OrderValidity.Day, Side.Sell, 5, 100);
            var bookEvents =
                Book.CreateOrder("Company3", "Order3", OrderValidity.Day, Side.Sell,3,  100);

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
            Book.CreateOrder("Company4", "Order4", OrderValidity.Day, Side.Sell, 5, 100);
            var bookEvents =
                Book.CreateOrder("Company5", "Order5", OrderValidity.Day, Side.Sell, 3, 110);

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
            Book.CreateOrder("Company6", "Order6", OrderValidity.Day, Side.Buy, 5, 100);
            var bookEvents = Book.CreateOrder("Company7", "Order7", OrderValidity.Day, Side.Buy, 3, 110);

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
            Book.CreateOrder("Company8", "Order8", OrderValidity.Day, Side.Buy, 5, 100);
            var bookEvents =
                Book.CreateOrder("Company9", "Order9", OrderValidity.Day, Side.Sell, 3, 110);

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
            Book.CreateOrder("Company10", "Order10", OrderValidity.Day, Side.Buy, 3, 110);
            Book.CreateOrder("Company11", "Order11", OrderValidity.Day, Side.Buy, 4, 120);
            var bookEvents = Book.CreateOrder("Company12", "Order12", OrderValidity.Day, Side.Buy, 5, 130);

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
            Book.CreateOrder("Company13", "Order13", OrderValidity.Day, Side.Buy, 3, 110);
            Book.CreateOrder("Company14", "Order14", OrderValidity.Day, Side.Buy, 4, 120);
            var bookEvents =
                Book.CreateOrder("Company15", "Order15", OrderValidity.Day, Side.Sell, 5, 100);

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