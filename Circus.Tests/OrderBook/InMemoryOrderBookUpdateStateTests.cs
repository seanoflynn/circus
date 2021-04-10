using System;
using Circus.OrderBook;
using Circus.TimeProviders;
using NUnit.Framework;

namespace Circus.Tests.OrderBook
{
    [TestFixture]
    public class InMemoryOrderBookUpdateStateTests
    {
        private static readonly Security Sec = new("GCZ6", SecurityType.Future, 10, 10);

        private static readonly DateTime Now1 = new(2000, 1, 1, 12, 0, 0);
        private static readonly DateTime Now2 = new(2000, 1, 1, 12, 1, 0);
        private static readonly DateTime Now3 = new(2000, 1, 1, 12, 2, 0);
        
        private static readonly Guid ClientId1 = Guid.NewGuid();
        private static readonly Guid ClientId2 = Guid.NewGuid();
        private static readonly Guid ClientId3 = Guid.NewGuid();

        private static readonly Guid OrderId1 = Guid.NewGuid();
        private static readonly Guid OrderId2 = Guid.NewGuid();
        private static readonly Guid OrderId3 = Guid.NewGuid();
        
        private static TestTimeProvider TimeProvider;
        private static IOrderBook Book;

        [SetUp]
        public void SetUp()
        {
            TimeProvider = new TestTimeProvider(Now1);
            Book = new InMemoryOrderBook(Sec, TimeProvider);
        }

        [TestCase(OrderBookStatus.PreOpen)]
        [TestCase(OrderBookStatus.Open)]
        [TestCase(OrderBookStatus.Closed)]
        public void Valid_Success(OrderBookStatus status)
        {
            // act
            var events = Book.UpdateStatus(status);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var statusChanged = events[0] as StatusChanged;
            Assert.IsNotNull(statusChanged);
            Assert.AreEqual(Sec, statusChanged.Security);
            Assert.AreEqual(status, statusChanged.Status);
            Assert.AreEqual(Now1, statusChanged.Time);
        }

        [Test]
        public void Open_MatchPreOpenOrders()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.PreOpen);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 5, 100);
            TimeProvider.SetCurrentTime(Now2);
            Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 5, 100);
            TimeProvider.SetCurrentTime(Now3);

            // act
            var events = Book.UpdateStatus(OrderBookStatus.Open);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(2, events.Count);
            var matched = events[1] as OrdersMatched;
            Assert.IsNotNull(matched);
            Assert.AreEqual(Sec, matched.Security);
            Assert.AreEqual(Now3, matched.Time);
            Assert.AreEqual(100, matched.Price);
            Assert.AreEqual(5, matched.Quantity);
            Assert.IsNotNull(matched.Fills);
            Assert.AreEqual(2, matched.Fills.Count);

            Assert.AreEqual(Sec, matched.Fills[0].Security);
            Assert.AreEqual(Now3, matched.Fills[0].Time);
            Assert.AreEqual(ClientId1, matched.Fills[0].ClientId);
            Assert.AreEqual(OrderId1, matched.Fills[0].OrderId);
            Assert.AreEqual(100, matched.Fills[0].Price);
            Assert.AreEqual(5, matched.Fills[0].Quantity);
            Assert.AreEqual(true, matched.Fills[0].IsResting);
            Assert.AreEqual(ClientId1, matched.Fills[0].Order.ClientId);
            Assert.AreEqual(OrderId1, matched.Fills[0].Order.OrderId);
            Assert.AreEqual(Sec, matched.Fills[0].Order.Security);
            Assert.AreEqual(Now1, matched.Fills[0].Order.CreatedTime);
            Assert.AreEqual(Now1, matched.Fills[0].Order.ModifiedTime);
            Assert.AreEqual(Now3, matched.Fills[0].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched.Fills[0].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched.Fills[0].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched.Fills[0].Order.OrderValidity);
            Assert.AreEqual(Side.Buy, matched.Fills[0].Order.Side);
            Assert.AreEqual(100, matched.Fills[0].Order.Price);
            Assert.IsNull(matched.Fills[0].Order.TriggerPrice);
            Assert.AreEqual(5, matched.Fills[0].Order.Quantity);
            Assert.AreEqual(5, matched.Fills[0].Order.FilledQuantity);
            Assert.AreEqual(0, matched.Fills[0].Order.RemainingQuantity);

            Assert.AreEqual(Sec, matched.Fills[1].Security);
            Assert.AreEqual(Now3, matched.Fills[1].Time);
            Assert.AreEqual(ClientId2, matched.Fills[1].ClientId);
            Assert.AreEqual(OrderId2, matched.Fills[1].OrderId);
            Assert.AreEqual(100, matched.Fills[1].Price);
            Assert.AreEqual(5, matched.Fills[1].Quantity);
            Assert.AreEqual(false, matched.Fills[1].IsResting);
            Assert.AreEqual(ClientId2, matched.Fills[1].Order.ClientId);
            Assert.AreEqual(OrderId2, matched.Fills[1].Order.OrderId);
            Assert.AreEqual(Sec, matched.Fills[1].Order.Security);
            Assert.AreEqual(Now2, matched.Fills[1].Order.CreatedTime);
            Assert.AreEqual(Now2, matched.Fills[1].Order.ModifiedTime);
            Assert.AreEqual(Now3, matched.Fills[1].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched.Fills[1].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched.Fills[1].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched.Fills[1].Order.OrderValidity);
            Assert.AreEqual(Side.Sell, matched.Fills[1].Order.Side);
            Assert.AreEqual(100, matched.Fills[1].Order.Price);
            Assert.IsNull(matched.Fills[1].Order.TriggerPrice);
            Assert.AreEqual(5, matched.Fills[1].Order.Quantity);
            Assert.AreEqual(5, matched.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(0, matched.Fills[1].Order.RemainingQuantity);
        }

        [Test]
        public void Closed_ExpireDayLimitOrders()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 5, 100);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.UpdateStatus(OrderBookStatus.Closed);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(2, events.Count);

            var expired = events[1] as ExpireOrderConfirmed;
            Assert.IsNotNull(expired);
            Assert.AreEqual(Sec, expired.Security);
            Assert.AreEqual(Now2, expired.Time);
            Assert.AreEqual(ClientId1, expired.ClientId);
            Assert.AreEqual(ClientId1, expired.Order.ClientId);
            Assert.AreEqual(OrderId1, expired.Order.OrderId);
            Assert.AreEqual(Sec, expired.Order.Security);
            Assert.AreEqual(Now1, expired.Order.CreatedTime);
            Assert.AreEqual(Now1, expired.Order.ModifiedTime);
            Assert.AreEqual(Now2, expired.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Expired, expired.Order.Status);
            Assert.AreEqual(OrderType.Limit, expired.Order.Type);
            Assert.AreEqual(OrderValidity.Day, expired.Order.OrderValidity);
            Assert.AreEqual(Side.Buy, expired.Order.Side);
            Assert.AreEqual(100, expired.Order.Price);
            Assert.IsNull(expired.Order.TriggerPrice);
            Assert.AreEqual(5, expired.Order.Quantity);
            Assert.AreEqual(0, expired.Order.FilledQuantity);
            Assert.AreEqual(0, expired.Order.RemainingQuantity);
        }

        [Test]
        public void Closed_ExpireDayStopOrders()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 5, 100);
            Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 5, 100);
            Book.CreateOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Sell, 5, null, 90);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.UpdateStatus(OrderBookStatus.Closed);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(2, events.Count);

            var expired = events[1] as ExpireOrderConfirmed;
            Assert.IsNotNull(expired);
            Assert.AreEqual(Sec, expired.Security);
            Assert.AreEqual(Now2, expired.Time);
            Assert.AreEqual(ClientId3, expired.ClientId);
            Assert.AreEqual(ClientId3, expired.Order.ClientId);
            Assert.AreEqual(OrderId3, expired.Order.OrderId);
            Assert.AreEqual(Sec, expired.Order.Security);
            Assert.AreEqual(Now1, expired.Order.CreatedTime);
            Assert.AreEqual(Now1, expired.Order.ModifiedTime);
            Assert.AreEqual(Now2, expired.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Expired, expired.Order.Status);
            Assert.AreEqual(OrderType.StopMarket, expired.Order.Type);
            Assert.AreEqual(OrderValidity.Day, expired.Order.OrderValidity);
            Assert.AreEqual(Side.Sell, expired.Order.Side);
            Assert.IsNull(expired.Order.Price);
            Assert.AreEqual(90, expired.Order.TriggerPrice);
            Assert.AreEqual(5, expired.Order.Quantity);
            Assert.AreEqual(0, expired.Order.FilledQuantity);
            Assert.AreEqual(0, expired.Order.RemainingQuantity);
        }

        [Test]
        public void Closed_DontExpireGoodTilCanceledOrders()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.GoodTilCanceled, Side.Buy, 5, 100);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.UpdateStatus(OrderBookStatus.Closed);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);

            var statusChanged = events[0] as StatusChanged;
            Assert.IsNotNull(statusChanged);
            Assert.AreEqual(Sec, statusChanged.Security);
            Assert.AreEqual(OrderBookStatus.Closed, statusChanged.Status);
            Assert.AreEqual(Now2, statusChanged.Time);
        }
    }
}