using System;
using Circus.OrderBook;
using Circus.TimeProviders;
using NUnit.Framework;

namespace Circus.Tests.OrderBook
{
    [TestFixture]
    public class InMemoryOrderBookFillOrKillTests
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

        [Test]
        public void LimitOrder_SufficientLiquidityAtSingleLevel_FullyFilled()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(CompanyId1, OrderId1, OrderValidity.Day, Side.Sell, 5, 100);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.CreateOrder(CompanyId2, OrderId2, OrderValidity.FillOrKill, Side.Buy, 5, 100);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(2, events.Count);

            var matched = events[1] as OrdersMatched;
            Assert.IsNotNull(matched);
            Assert.AreEqual(100, matched.Price);
            Assert.AreEqual(5, matched.Quantity);
            Assert.AreEqual(OrderStatus.Filled, matched.Fills[1].Order.Status);
            Assert.AreEqual(5, matched.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(0, matched.Fills[1].Order.RemainingQuantity);
        }

        [Test]
        public void LimitOrder_SufficientLiquidityAcrossMultipleLevels_FullyFilled()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(CompanyId1, OrderId1, OrderValidity.Day, Side.Sell, 3, 100);
            TimeProvider.SetCurrentTime(Now2);
            Book.CreateOrder(CompanyId2, OrderId2, OrderValidity.Day, Side.Sell, 3, 110);
            TimeProvider.SetCurrentTime(Now3);

            // act - only fills if the 3@100 and 2@110 levels are summed together
            var events = Book.CreateOrder(CompanyId3, OrderId3, OrderValidity.FillOrKill, Side.Buy, 5, 110);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(3, events.Count);

            var matched1 = events[1] as OrdersMatched;
            Assert.IsNotNull(matched1);
            Assert.AreEqual(100, matched1.Price);
            Assert.AreEqual(3, matched1.Quantity);

            var matched2 = events[2] as OrdersMatched;
            Assert.IsNotNull(matched2);
            Assert.AreEqual(110, matched2.Price);
            Assert.AreEqual(2, matched2.Quantity);
            Assert.AreEqual(OrderStatus.Filled, matched2.Fills[1].Order.Status);
            Assert.AreEqual(OrderId3, matched2.Fills[1].ClientOrderId);
            Assert.AreEqual(5, matched2.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(0, matched2.Fills[1].Order.RemainingQuantity);
        }

        [Test]
        public void LimitOrder_InsufficientLiquidity_Rejected()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(CompanyId1, OrderId1, OrderValidity.Day, Side.Sell, 2, 100);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.CreateOrder(CompanyId2, OrderId2, OrderValidity.FillOrKill, Side.Buy, 5, 100);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);

            var rejected = events[0] as CreateOrderRejected;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(Sec, rejected.Security);
            Assert.AreEqual(Now2, rejected.Time);
            Assert.AreEqual(CompanyId2, rejected.CompanyId);
            Assert.AreEqual(OrderId2, rejected.ClientOrderId);
            Assert.AreEqual(OrderRejectedReason.InsufficientLiquidityForFillOrKill, rejected.Reason);

            // book is completely untouched - no partial fill leaked through
            var levels = Book.GetLevels(Side.Sell, 10);
            Assert.AreEqual(1, levels.Count);
            Assert.AreEqual(100, levels[0].Price);
            Assert.AreEqual(2, levels[0].Quantity);
            Assert.AreEqual(0, Book.GetLevels(Side.Buy, 10).Count);
        }

        [Test]
        public void StopLimitOrder_TriggersWithInsufficientLiquidity_Cancelled()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(CompanyId1, OrderId1, OrderValidity.Day, Side.Sell, 5, 500);
            Book.CreateOrder(CompanyId2, OrderId2, OrderValidity.Day, Side.Buy, 5, 500); // last traded price = 500
            TimeProvider.SetCurrentTime(Now2);

            // FOK stop-limit buy: triggers when price rises to/above 520, then willing to pay up to 530
            Book.CreateOrder(CompanyId3, OrderId3, OrderValidity.FillOrKill, Side.Buy, 5, 530, 520);
            TimeProvider.SetCurrentTime(Now3);

            // only 2 available - not enough to fill the stop's 5 in full
            Book.CreateOrder(CompanyId4, OrderId4, OrderValidity.Day, Side.Sell, 2, 530);
            TimeProvider.SetCurrentTime(Now4);
            Book.CreateOrder(CompanyId5, OrderId5, OrderValidity.Day, Side.Buy, 1, 520);
            TimeProvider.SetCurrentTime(Now5);

            // act - trade at 520 triggers the stop
            var events = Book.CreateOrder(CompanyId6, OrderId6, OrderValidity.Day, Side.Sell, 1, 520);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(3, events.Count);

            Assert.IsInstanceOf<CreateOrderConfirmed>(events[0]);
            Assert.IsInstanceOf<OrdersMatched>(events[1]);

            var cancelled = events[2] as CancelOrderConfirmed;
            Assert.IsNotNull(cancelled);
            Assert.AreEqual(OrderId3, cancelled.Order.ClientOrderId);
            Assert.AreEqual(OrderCancelledReason.FillOrKillNotFilled, cancelled.Reason);
            Assert.AreEqual(OrderStatus.Cancelled, cancelled.Order.Status);
            Assert.AreEqual(OrderType.StopLimit, cancelled.Order.Type);
            Assert.AreEqual(OrderValidity.FillOrKill, cancelled.Order.OrderValidity);
            Assert.AreEqual(0, cancelled.Order.FilledQuantity);
            Assert.AreEqual(0, cancelled.Order.RemainingQuantity);

            // the 2@530 resting sell was never touched
            var levels = Book.GetLevels(Side.Sell, 10);
            Assert.AreEqual(1, levels.Count);
            Assert.AreEqual(530, levels[0].Price);
            Assert.AreEqual(2, levels[0].Quantity);
        }
    }
}
