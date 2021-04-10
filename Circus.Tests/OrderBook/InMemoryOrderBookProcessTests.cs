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
            // act
            Book.Process(new CreateOrder(Sec, ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3, 100));
        }

        [Test]
        public void Process_UpdateOrder_Success()
        {
            // act
            Book.Process(new UpdateOrder(Sec, ClientId1, OrderId1, 3));
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