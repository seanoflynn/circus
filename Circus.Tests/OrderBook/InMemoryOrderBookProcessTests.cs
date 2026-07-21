using System;
using Circus.OrderBook;
using Circus.TimeProviders;
using NUnit.Framework;

namespace Circus.Tests.OrderBook
{
    [TestFixture]
    public class InMemoryOrderBookProcessTests
    {
        private static readonly Security Sec = new("GCZ6", SecurityType.Future, 10, 10);
        private static readonly DateTime Now1 = new(2000, 1, 1, 12, 0, 0);
        private static readonly Guid ClientId1 = Guid.NewGuid();
        private static readonly Guid OrderId1 = Guid.NewGuid();

        private static TestTimeProvider TimeProvider;
        private static IOrderBook Book;

        [SetUp]
        public void SetUp()
        {
            TimeProvider = new TestTimeProvider(Now1);
            Book = new InMemoryOrderBook(Sec, TimeProvider);
        }

        [Test]
        public void Process_CreateOrder_Success()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);

            // act - only Price set, no TriggerPrice: this must rest as a plain working limit
            // order, not get misrouted into the TriggerPrice slot as a hidden stop order
            var events = Book.Process(new CreateOrder(Sec, ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3, 100));

            // assert
            Assert.IsNotNull(events);
            var created = events[0] as CreateOrderConfirmed;
            Assert.IsNotNull(created);
            Assert.AreEqual(OrderStatus.Working, created.Order.Status);
            Assert.AreEqual(OrderType.Limit, created.Order.Type);
            Assert.AreEqual(100, created.Order.Price);
            Assert.IsNull(created.Order.TriggerPrice);
            Assert.AreEqual(1, Book.GetLevels(Side.Buy, 10).Count);
        }

        [Test]
        public void Process_UpdateOrder_Success()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3, 100);

            // act - only Price set, no TriggerPrice: this must actually reprice the order
            var events = Book.Process(new UpdateOrder(Sec, ClientId1, OrderId1, Price: 110));

            // assert
            Assert.IsNotNull(events);
            var updated = events[0] as UpdateOrderConfirmed;
            Assert.IsNotNull(updated);
            Assert.AreEqual(110, updated.Order.Price);
            Assert.IsNull(updated.Order.TriggerPrice);
        }

        [Test]
        public void Process_CancelOrder_Success()
        {
            // act
            Book.Process(new CancelOrder(Sec, ClientId1, OrderId1));
        }

        [Test]
        public void Process_UpdateStatus_Success()
        {
            // act
            Book.Process(new UpdateStatus(Sec, OrderBookStatus.Open));
        }
    }
}