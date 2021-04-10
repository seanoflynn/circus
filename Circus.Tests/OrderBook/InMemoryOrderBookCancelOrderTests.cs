using System;
using Circus.OrderBook;
using Circus.TimeProviders;
using NUnit.Framework;

namespace Circus.Tests.OrderBook
{
    [TestFixture]
    public class InMemoryOrderBookCancelOrderTests
    {
        private static readonly Security Sec = new("GCZ6", SecurityType.Future, 10, 10);

        private static readonly DateTime Now1 = new(2000, 1, 1, 12, 0, 0);
        private static readonly DateTime Now2 = new(2000, 1, 1, 12, 1, 0);

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

        [Test]
        public void LimitOrder_Success()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3, 100);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.CancelOrder(ClientId1, OrderId1);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var cancelled = events[0] as CancelOrderConfirmed;
            Assert.IsNotNull(cancelled);
            Assert.AreEqual(Sec, cancelled.Security);
            Assert.AreEqual(Now2, cancelled.Time);
            Assert.AreEqual(ClientId1, cancelled.ClientId);
            Assert.AreEqual(OrderCancelledReason.Cancelled, cancelled.Reason);
            Assert.AreEqual(ClientId1, cancelled.Order.ClientId);
            Assert.AreEqual(OrderId1, cancelled.Order.OrderId);
            Assert.AreEqual(Sec, cancelled.Order.Security);
            Assert.AreEqual(Now1, cancelled.Order.CreatedTime);
            Assert.AreEqual(Now1, cancelled.Order.ModifiedTime);
            Assert.AreEqual(Now2, cancelled.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Cancelled, cancelled.Order.Status);
            Assert.AreEqual(OrderType.Limit, cancelled.Order.Type);
            Assert.AreEqual(OrderValidity.Day, cancelled.Order.OrderValidity);
            Assert.AreEqual(Side.Buy, cancelled.Order.Side);
            Assert.AreEqual(100, cancelled.Order.Price);
            Assert.IsNull(cancelled.Order.TriggerPrice);
            Assert.AreEqual(3, cancelled.Order.Quantity);
            Assert.AreEqual(0, cancelled.Order.FilledQuantity);
            Assert.AreEqual(0, cancelled.Order.RemainingQuantity);
        }

        [Test]
        public void StopOrder_Success()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 5, 100);
            Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 5, 100);
            Book.CreateOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Buy, 3, null, 110);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.CancelOrder(ClientId3, OrderId3);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var cancelled = events[0] as CancelOrderConfirmed;
            Assert.IsNotNull(cancelled);
            Assert.AreEqual(Sec, cancelled.Security);
            Assert.AreEqual(Now2, cancelled.Time);
            Assert.AreEqual(ClientId3, cancelled.ClientId);
            Assert.AreEqual(OrderCancelledReason.Cancelled, cancelled.Reason);
            Assert.AreEqual(ClientId3, cancelled.Order.ClientId);
            Assert.AreEqual(OrderId3, cancelled.Order.OrderId);
            Assert.AreEqual(Sec, cancelled.Order.Security);
            Assert.AreEqual(Now1, cancelled.Order.CreatedTime);
            Assert.AreEqual(Now1, cancelled.Order.ModifiedTime);
            Assert.AreEqual(Now2, cancelled.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Cancelled, cancelled.Order.Status);
            Assert.AreEqual(OrderType.StopMarket, cancelled.Order.Type);
            Assert.AreEqual(OrderValidity.Day, cancelled.Order.OrderValidity);
            Assert.AreEqual(Side.Buy, cancelled.Order.Side);
            Assert.IsNull(cancelled.Order.Price);
            Assert.AreEqual(110, cancelled.Order.TriggerPrice);
            Assert.AreEqual(3, cancelled.Order.Quantity);
            Assert.AreEqual(0, cancelled.Order.FilledQuantity);
            Assert.AreEqual(0, cancelled.Order.RemainingQuantity);
        }

        [Test]
        public void MarketClosed_Rejected()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3, 100);
            Book.UpdateStatus(OrderBookStatus.Closed);

            // act
            var events = Book.CancelOrder(ClientId1, OrderId1);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as CancelOrderRejected;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(Sec, rejected.Security);
            Assert.AreEqual(Now1, rejected.Time);
            Assert.AreEqual(ClientId1, rejected.ClientId);
            Assert.AreEqual(OrderId1, rejected.OrderId);
            Assert.AreEqual(OrderRejectedReason.MarketClosed, rejected.Reason);
        }
        
        [Test]
        public void Completed_Rejected()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3, 100);
            Book.CancelOrder(ClientId1, OrderId1);

            // act
            var events = Book.CancelOrder(ClientId1, OrderId1);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as CancelOrderRejected;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(Sec, rejected.Security);
            Assert.AreEqual(Now1, rejected.Time);
            Assert.AreEqual(ClientId1, rejected.ClientId);
            Assert.AreEqual(OrderId1, rejected.OrderId);
            Assert.AreEqual(OrderRejectedReason.TooLateToCancel, rejected.Reason);
        }

        [Test]
        public void NotFound_Rejected()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);

            // act
            var events = Book.CancelOrder(ClientId1, OrderId1);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as CancelOrderRejected;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(Sec, rejected.Security);
            Assert.AreEqual(Now1, rejected.Time);
            Assert.AreEqual(ClientId1, rejected.ClientId);
            Assert.AreEqual(OrderId1, rejected.OrderId);
            Assert.AreEqual(OrderRejectedReason.OrderNotInBook, rejected.Reason);
        }
    }
}