using System;
using Circus.OrderBook;
using Circus.TimeProviders;
using NUnit.Framework;

namespace Circus.Tests.OrderBook
{
    [TestFixture]
    public class InMemoryOrderBookFillAndKillTests
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
        public void LimitOrder_FullFill_Success()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 3, 100);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.FillAndKill(), Side.Buy, 3, 100);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(2, events.Count);

            var matched = events[1] as OrdersMatched;
            Assert.IsNotNull(matched);
            Assert.AreEqual(100, matched.Price);
            Assert.AreEqual(3, matched.Quantity);

            Assert.AreEqual(OrderId2, matched.Fills[1].ClientOrderId);
            Assert.AreEqual(OrderStatus.Filled, matched.Fills[1].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched.Fills[1].Order.Type);
            Assert.AreEqual(new OrderValidity.FillAndKill(), matched.Fills[1].Order.OrderValidity);
            Assert.AreEqual(3, matched.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(0, matched.Fills[1].Order.RemainingQuantity);
        }

        [Test]
        public void LimitOrder_PartialFill_RemainderCancelled()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 2, 100);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.FillAndKill(), Side.Buy, 5, 100);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(3, events.Count);

            var matched = events[1] as OrdersMatched;
            Assert.IsNotNull(matched);
            Assert.AreEqual(100, matched.Price);
            Assert.AreEqual(2, matched.Quantity);

            var cancelled = events[2] as CancelOrderConfirmed;
            Assert.IsNotNull(cancelled);
            Assert.AreEqual(Sec, cancelled.Security);
            Assert.AreEqual(Now2, cancelled.Time);
            Assert.AreEqual(CompanyId2, cancelled.CompanyId);
            Assert.AreEqual(OrderCancelledReason.FillAndKillNotFilled, cancelled.Reason);
            Assert.AreEqual(OrderId2, cancelled.Order.ClientOrderId);
            Assert.AreEqual(OrderStatus.Cancelled, cancelled.Order.Status);
            Assert.AreEqual(OrderType.Limit, cancelled.Order.Type);
            Assert.AreEqual(new OrderValidity.FillAndKill(), cancelled.Order.OrderValidity);
            Assert.AreEqual(5, cancelled.Order.Quantity);
            Assert.AreEqual(2, cancelled.Order.FilledQuantity);
            Assert.AreEqual(0, cancelled.Order.RemainingQuantity);

            // book has nothing resting from either order
            Assert.AreEqual(0, Book.GetLevels(Side.Buy, 10).Count);
            Assert.AreEqual(0, Book.GetLevels(Side.Sell, 10).Count);
        }

        [Test]
        public void LimitOrder_EmptyBook_ImmediatelyCancelled()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);

            // act
            var events = Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.FillAndKill(), Side.Buy, 5, 100);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(2, events.Count);

            var created = events[0] as CreateOrderConfirmed;
            Assert.IsNotNull(created);

            var cancelled = events[1] as CancelOrderConfirmed;
            Assert.IsNotNull(cancelled);
            Assert.AreEqual(OrderCancelledReason.FillAndKillNotFilled, cancelled.Reason);
            Assert.AreEqual(OrderStatus.Cancelled, cancelled.Order.Status);
            Assert.AreEqual(0, cancelled.Order.FilledQuantity);
            Assert.AreEqual(0, cancelled.Order.RemainingQuantity);

            Assert.AreEqual(0, Book.GetLevels(Side.Buy, 10).Count);
        }

        [Test]
        public void MarketOrder_PartialFillWithinProtection_RemainderCancelled()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10, 20);
            var book = new InMemoryOrderBook(sec, TimeProvider);
            book.UpdateStatus(OrderBookStatus.Open);
            book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 2, 500);
            TimeProvider.SetCurrentTime(Now2);

            // act
            // NB: a Market + GTC/Day order in the same situation would instead rest the
            // remainder as a limit order at the protected price (see MarketOrder_Success).
            var events = book.CreateMarketOrder(CompanyId2, OrderId2, new OrderValidity.FillAndKill(), Side.Buy, 5);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(3, events.Count);

            var matched = events[1] as OrdersMatched;
            Assert.IsNotNull(matched);
            Assert.AreEqual(500, matched.Price);
            Assert.AreEqual(2, matched.Quantity);

            var cancelled = events[2] as CancelOrderConfirmed;
            Assert.IsNotNull(cancelled);
            Assert.AreEqual(OrderCancelledReason.FillAndKillNotFilled, cancelled.Reason);
            Assert.AreEqual(OrderId2, cancelled.Order.ClientOrderId);
            Assert.AreEqual(OrderType.Market, cancelled.Order.Type);
            Assert.AreEqual(700, cancelled.Order.Price);
            Assert.AreEqual(OrderStatus.Cancelled, cancelled.Order.Status);
            Assert.AreEqual(2, cancelled.Order.FilledQuantity);
            Assert.AreEqual(0, cancelled.Order.RemainingQuantity);

            Assert.AreEqual(0, book.GetLevels(Side.Buy, 10).Count);
        }

        [Test]
        public void StopLimitOrder_TriggersAndPartiallyFills_RemainderCancelled()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 5, 500);
            Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Buy, 5, 500); // last traded price = 500
            TimeProvider.SetCurrentTime(Now2);

            // FAK stop-limit buy: triggers when price rises to/above 520, then willing to pay up to 530
            Book.CreateStopLimitOrder(CompanyId3, OrderId3, new OrderValidity.FillAndKill(), Side.Buy, 5, 530, 520);
            TimeProvider.SetCurrentTime(Now3);

            // only 2 available to fill the stop once triggered
            Book.CreateLimitOrder(CompanyId4, OrderId4, new OrderValidity.Day(), Side.Sell, 2, 530);
            TimeProvider.SetCurrentTime(Now4);
            Book.CreateLimitOrder(CompanyId5, OrderId5, new OrderValidity.Day(), Side.Buy, 1, 520);
            TimeProvider.SetCurrentTime(Now5);

            // act - trade at 520 triggers the stop
            var events = Book.CreateLimitOrder(CompanyId6, OrderId6, new OrderValidity.Day(), Side.Sell, 1, 520);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(5, events.Count);

            Assert.IsInstanceOf<CreateOrderConfirmed>(events[0]);
            Assert.IsInstanceOf<OrdersMatched>(events[1]);

            var triggered = events[2] as UpdateOrderConfirmed;
            Assert.IsNotNull(triggered);
            Assert.AreEqual(OrderId3, triggered.Order.ClientOrderId);
            Assert.AreEqual(OrderType.Limit, triggered.Order.Type);
            Assert.AreEqual(530, triggered.Order.Price);

            var stopMatch = events[3] as OrdersMatched;
            Assert.IsNotNull(stopMatch);
            Assert.AreEqual(530, stopMatch.Price);
            Assert.AreEqual(2, stopMatch.Quantity);

            var cancelled = events[4] as CancelOrderConfirmed;
            Assert.IsNotNull(cancelled);
            Assert.AreEqual(OrderId3, cancelled.Order.ClientOrderId);
            Assert.AreEqual(OrderCancelledReason.FillAndKillNotFilled, cancelled.Reason);
            Assert.AreEqual(OrderStatus.Cancelled, cancelled.Order.Status);
            Assert.AreEqual(OrderType.Limit, cancelled.Order.Type);
            Assert.AreEqual(new OrderValidity.FillAndKill(), cancelled.Order.OrderValidity);
            Assert.AreEqual(2, cancelled.Order.FilledQuantity);
            Assert.AreEqual(0, cancelled.Order.RemainingQuantity);

            Assert.AreEqual(0, Book.GetLevels(Side.Buy, 10).Count);
            Assert.AreEqual(0, Book.GetLevels(Side.Sell, 10).Count);
        }
    }
}
