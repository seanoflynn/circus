using System;
using Circus.OrderBook;
using Circus.TimeProviders;
using NUnit.Framework;

namespace Circus.Tests.OrderBook
{
    [TestFixture]
    public class InMemoryOrderBookMinQuantityTests
    {
        private static readonly Security Sec = new("GCZ6", SecurityType.Future, 10, 10);

        private static readonly DateTime Now1 = new(2000, 1, 1, 12, 0, 0);
        private static readonly DateTime Now2 = new(2000, 1, 1, 12, 1, 0);
        private static readonly DateTime Now3 = new(2000, 1, 1, 12, 2, 0);
        private static readonly DateTime Now4 = new(2000, 1, 1, 12, 3, 0);
        private static readonly DateTime Now5 = new(2000, 1, 1, 12, 4, 0);

        private static readonly string CompanyId1 = "Company1";
        private static readonly string CompanyId2 = "Company2";
        private static readonly string CompanyId3 = "Company3";
        private static readonly string CompanyId4 = "Company4";
        private static readonly string CompanyId5 = "Company5";
        private static readonly string CompanyId6 = "Company6";

        private static readonly string OrderId1 = "Order1";
        private static readonly string OrderId2 = "Order2";
        private static readonly string OrderId3 = "Order3";
        private static readonly string OrderId4 = "Order4";
        private static readonly string OrderId5 = "Order5";
        private static readonly string OrderId6 = "Order6";

        private static TestTimeProvider TimeProvider;
        private static IOrderBook Book;

        [SetUp]
        public void SetUp()
        {
            TimeProvider = new TestTimeProvider(Now1);
            Book = new InMemoryOrderBook(Sec, TimeProvider);
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(6)]
        public void MinQuantity_OutsideValidRange_Rejected(int minQuantity)
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);

            // act
            var events = Book.CreateLimitOrder(CompanyId1, OrderId1,
                new OrderValidity.FillAndKill { MinQuantity = minQuantity }, Side.Buy, 5, 100);

            // assert
            var rejected = events[0] as CreateOrderRejected;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(OrderRejectedReason.QuantityOutOfRange, rejected.Reason);
        }

        [Test]
        public void SufficientForMinQuantity_ButNotFullSize_FillsAvailable_CancelsRemainder()
        {
            // arrange - only 3 available, less than the order's full size of 5, but enough to
            // satisfy a MinQuantity of 3
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 3, 100);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.CreateLimitOrder(CompanyId2, OrderId2,
                new OrderValidity.FillAndKill { MinQuantity = 3 }, Side.Buy, 5, 100);

            // assert - proceeds like an ordinary FillAndKill once the gate is satisfied: fills
            // what's available, cancels the rest
            Assert.AreEqual(3, events.Count);
            Assert.IsInstanceOf<CreateOrderConfirmed>(events[0]);

            var matched = events[1] as OrdersMatched;
            Assert.IsNotNull(matched);
            Assert.AreEqual(3, matched.Quantity);

            var cancelled = events[2] as CancelOrderConfirmed;
            Assert.IsNotNull(cancelled);
            Assert.AreEqual(OrderCancelledReason.FillAndKillNotFilled, cancelled.Reason);
            Assert.AreEqual(3, cancelled.Order.FilledQuantity);
            Assert.AreEqual(0, cancelled.Order.RemainingQuantity);
        }

        [Test]
        public void InsufficientForMinQuantity_RejectedOutright_NothingFills()
        {
            // arrange - only 2 available, below the MinQuantity of 3
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 2, 100);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.CreateLimitOrder(CompanyId2, OrderId2,
                new OrderValidity.FillAndKill { MinQuantity = 3 }, Side.Buy, 5, 100);

            // assert - rejected outright, not even a partial fill below the minimum
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as CreateOrderRejected;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(OrderRejectedReason.InsufficientLiquidityForMinQuantity, rejected.Reason);

            // the resting sell is completely untouched
            var sellLevels = Book.GetLevels(Side.Sell, 10);
            Assert.AreEqual(1, sellLevels.Count);
            Assert.AreEqual(2, sellLevels[0].Quantity);
            Assert.AreEqual(0, Book.GetLevels(Side.Buy, 10).Count);
        }

        [Test]
        public void TriggeredFillAndKillStop_WithMinQuantity_GoesThroughSameGate()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 5, 500);
            Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Buy, 5, 500); // last traded price = 500
            TimeProvider.SetCurrentTime(Now2);

            // FAK stop-limit buy: triggers at/above 520, willing to pay up to 530, needs at least 2
            Book.CreateStopLimitOrder(CompanyId3, OrderId3, new OrderValidity.FillAndKill { MinQuantity = 2 },
                Side.Buy, 5, 530, 520);
            TimeProvider.SetCurrentTime(Now3);

            // only 1 available once the stop triggers - below its MinQuantity of 2
            Book.CreateLimitOrder(CompanyId4, OrderId4, new OrderValidity.Day(), Side.Sell, 1, 530);
            TimeProvider.SetCurrentTime(Now4);
            Book.CreateLimitOrder(CompanyId5, OrderId5, new OrderValidity.Day(), Side.Buy, 1, 520);
            TimeProvider.SetCurrentTime(Now5);

            // act - a sell crossing the just-rested 520 buy prints a trade at 520, triggering the stop
            var events = Book.CreateLimitOrder(CompanyId6, OrderId6, new OrderValidity.Day(), Side.Sell, 1, 520);

            // assert - stop triggers, but the available 1 unit doesn't meet its MinQuantity of 2,
            // so it's cancelled directly rather than being converted to a working limit order
            // (same shape as the existing FillOrKill-insufficient-liquidity stop cancellation)
            Assert.AreEqual(3, events.Count);
            Assert.IsInstanceOf<CreateOrderConfirmed>(events[0]);
            Assert.IsInstanceOf<OrdersMatched>(events[1]);

            var cancelled = events[2] as CancelOrderConfirmed;
            Assert.IsNotNull(cancelled);
            Assert.AreEqual(OrderId3, cancelled.Order.ClientOrderId);
            Assert.AreEqual(OrderCancelledReason.FillAndKillNotFilled, cancelled.Reason);
            Assert.AreEqual(0, cancelled.Order.FilledQuantity);
            Assert.AreEqual(0, cancelled.Order.RemainingQuantity);

            // the 1 unit at 530 is untouched
            var sellLevels = Book.GetLevels(Side.Sell, 10);
            Assert.AreEqual(1, sellLevels.Count);
            Assert.AreEqual(530, sellLevels[0].Price);
            Assert.AreEqual(1, sellLevels[0].Quantity);
        }
    }
}
