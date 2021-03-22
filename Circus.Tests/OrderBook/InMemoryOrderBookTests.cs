using System;
using Circus.OrderBook;
using Circus.TimeProviders;
using NUnit.Framework;

namespace Circus.Tests.OrderBook
{
    [TestFixture]
    public class InMemoryOrderBookTests
    {
        private static readonly Security Sec = new("GCZ6", SecurityType.Future, 10, 10);

        private static readonly DateTime Now1 = new(2000, 1, 1, 12, 0, 0);
        private static readonly DateTime Now2 = new(2000, 1, 1, 12, 1, 0);
        private static readonly DateTime Now3 = new(2000, 1, 1, 12, 2, 0);
        private static readonly DateTime Now4 = new(2000, 1, 1, 12, 3, 0);

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
        public void CreateLimitOrder_Valid_Success()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);

            // act
            var events = Book.CreateLimitOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 100, 3);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);

            var created = events[0] as CreateOrderConfirmed;
            Assert.IsNotNull(created);
            Assert.AreEqual(Sec, created.Security);
            Assert.AreEqual(Now1, created.Time);
            Assert.AreEqual(ClientId1, created.ClientId);
            Assert.AreEqual(ClientId1, created.Order.ClientId);
            Assert.AreEqual(OrderId1, created.Order.OrderId);
            Assert.AreEqual(Sec, created.Order.Security);
            Assert.AreEqual(Now1, created.Order.CreatedTime);
            Assert.AreEqual(Now1, created.Order.ModifiedTime);
            Assert.IsNull(created.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, created.Order.Status);
            Assert.AreEqual(OrderType.Limit, created.Order.Type);
            Assert.AreEqual(OrderValidity.Day, created.Order.OrderValidity);
            Assert.AreEqual(Side.Buy, created.Order.Side);
            Assert.AreEqual(100, created.Order.Price);
            Assert.IsNull(created.Order.StopPrice);
            Assert.AreEqual(3, created.Order.Quantity);
            Assert.AreEqual(0, created.Order.FilledQuantity);
            Assert.AreEqual(3, created.Order.RemainingQuantity);
        }

        [Test]
        public void CreateLimitOrder_MatchAtSamePriceWithAggressorRemaining_Success()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 100, 3);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.CreateLimitOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 100, 5);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(2, events.Count);

            var matched = events[1] as OrdersMatched;
            Assert.IsNotNull(matched);
            Assert.AreEqual(Sec, matched.Security);
            Assert.AreEqual(Now2, matched.Time);
            Assert.AreEqual(100, matched.Price);
            Assert.AreEqual(3, matched.Quantity);
            Assert.IsNotNull(matched.Fills);
            Assert.AreEqual(2, matched.Fills.Count);

            Assert.AreEqual(Sec, matched.Fills[0].Security);
            Assert.AreEqual(Now2, matched.Fills[0].Time);
            Assert.AreEqual(ClientId1, matched.Fills[0].ClientId);
            Assert.AreEqual(OrderId1, matched.Fills[0].OrderId);
            Assert.AreEqual(100, matched.Fills[0].Price);
            Assert.AreEqual(3, matched.Fills[0].Quantity);
            Assert.AreEqual(true, matched.Fills[0].IsResting);
            Assert.AreEqual(ClientId1, matched.Fills[0].Order.ClientId);
            Assert.AreEqual(OrderId1, matched.Fills[0].Order.OrderId);
            Assert.AreEqual(Sec, matched.Fills[0].Order.Security);
            Assert.AreEqual(Now1, matched.Fills[0].Order.CreatedTime);
            Assert.AreEqual(Now1, matched.Fills[0].Order.ModifiedTime);
            Assert.AreEqual(Now2, matched.Fills[0].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched.Fills[0].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched.Fills[0].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched.Fills[0].Order.OrderValidity);
            Assert.AreEqual(Side.Buy, matched.Fills[0].Order.Side);
            Assert.AreEqual(100, matched.Fills[0].Order.Price);
            Assert.IsNull(matched.Fills[0].Order.StopPrice);
            Assert.AreEqual(3, matched.Fills[0].Order.Quantity);
            Assert.AreEqual(3, matched.Fills[0].Order.FilledQuantity);
            Assert.AreEqual(0, matched.Fills[0].Order.RemainingQuantity);

            Assert.AreEqual(Sec, matched.Fills[1].Security);
            Assert.AreEqual(Now2, matched.Fills[1].Time);
            Assert.AreEqual(ClientId2, matched.Fills[1].ClientId);
            Assert.AreEqual(OrderId2, matched.Fills[1].OrderId);
            Assert.AreEqual(100, matched.Fills[1].Price);
            Assert.AreEqual(3, matched.Fills[1].Quantity);
            Assert.AreEqual(false, matched.Fills[1].IsResting);
            Assert.AreEqual(ClientId2, matched.Fills[1].Order.ClientId);
            Assert.AreEqual(OrderId2, matched.Fills[1].Order.OrderId);
            Assert.AreEqual(Sec, matched.Fills[1].Order.Security);
            Assert.AreEqual(Now2, matched.Fills[1].Order.CreatedTime);
            Assert.AreEqual(Now2, matched.Fills[1].Order.ModifiedTime);
            Assert.IsNull(matched.Fills[1].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched.Fills[1].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched.Fills[1].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched.Fills[1].Order.OrderValidity);
            Assert.AreEqual(Side.Sell, matched.Fills[1].Order.Side);
            Assert.AreEqual(100, matched.Fills[1].Order.Price);
            Assert.IsNull(matched.Fills[1].Order.StopPrice);
            Assert.AreEqual(5, matched.Fills[1].Order.Quantity);
            Assert.AreEqual(3, matched.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(2, matched.Fills[1].Order.RemainingQuantity);
        }

        [Test]
        public void CreateLimitOrder_MatchAtDifferentPriceWithAggressorRemaining_Success()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 110, 3);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.CreateLimitOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 100, 5);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(2, events.Count);

            var matched = events[1] as OrdersMatched;
            Assert.IsNotNull(matched);
            Assert.AreEqual(Sec, matched.Security);
            Assert.AreEqual(Now2, matched.Time);
            Assert.AreEqual(110, matched.Price);
            Assert.AreEqual(3, matched.Quantity);
            Assert.IsNotNull(matched.Fills);
            Assert.AreEqual(2, matched.Fills.Count);

            Assert.AreEqual(Sec, matched.Fills[0].Security);
            Assert.AreEqual(Now2, matched.Fills[0].Time);
            Assert.AreEqual(ClientId1, matched.Fills[0].ClientId);
            Assert.AreEqual(OrderId1, matched.Fills[0].OrderId);
            Assert.AreEqual(110, matched.Fills[0].Price);
            Assert.AreEqual(3, matched.Fills[0].Quantity);
            Assert.AreEqual(true, matched.Fills[0].IsResting);
            Assert.AreEqual(ClientId1, matched.Fills[0].Order.ClientId);
            Assert.AreEqual(OrderId1, matched.Fills[0].Order.OrderId);
            Assert.AreEqual(Sec, matched.Fills[0].Order.Security);
            Assert.AreEqual(Now1, matched.Fills[0].Order.CreatedTime);
            Assert.AreEqual(Now1, matched.Fills[0].Order.ModifiedTime);
            Assert.AreEqual(Now2, matched.Fills[0].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched.Fills[0].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched.Fills[0].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched.Fills[0].Order.OrderValidity);
            Assert.AreEqual(Side.Buy, matched.Fills[0].Order.Side);
            Assert.AreEqual(110, matched.Fills[0].Order.Price);
            Assert.IsNull(matched.Fills[0].Order.StopPrice);
            Assert.AreEqual(3, matched.Fills[0].Order.Quantity);
            Assert.AreEqual(3, matched.Fills[0].Order.FilledQuantity);
            Assert.AreEqual(0, matched.Fills[0].Order.RemainingQuantity);
            
            Assert.AreEqual(Sec, matched.Fills[1].Security);
            Assert.AreEqual(Now2, matched.Fills[1].Time);
            Assert.AreEqual(ClientId2, matched.Fills[1].ClientId);
            Assert.AreEqual(OrderId2, matched.Fills[1].OrderId);
            Assert.AreEqual(110, matched.Fills[1].Price);
            Assert.AreEqual(3, matched.Fills[1].Quantity);
            Assert.AreEqual(false, matched.Fills[1].IsResting);
            Assert.AreEqual(ClientId2, matched.Fills[1].Order.ClientId);
            Assert.AreEqual(OrderId2, matched.Fills[1].Order.OrderId);
            Assert.AreEqual(Sec, matched.Fills[1].Order.Security);
            Assert.AreEqual(Now2, matched.Fills[1].Order.CreatedTime);
            Assert.AreEqual(Now2, matched.Fills[1].Order.ModifiedTime);
            Assert.IsNull(matched.Fills[1].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched.Fills[1].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched.Fills[1].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched.Fills[1].Order.OrderValidity);
            Assert.AreEqual(Side.Sell, matched.Fills[1].Order.Side);
            Assert.AreEqual(100, matched.Fills[1].Order.Price);
            Assert.IsNull(matched.Fills[1].Order.StopPrice);
            Assert.AreEqual(5, matched.Fills[1].Order.Quantity);
            Assert.AreEqual(3, matched.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(2, matched.Fills[1].Order.RemainingQuantity);
        }

        [Test]
        public void CreateLimitOrder_MatchAtDifferentPriceWithRestingRemaining_Success()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 110, 5);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.CreateLimitOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 100, 3);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(2, events.Count);

            var matched = events[1] as OrdersMatched;
            Assert.IsNotNull(matched);
            Assert.AreEqual(Sec, matched.Security);
            Assert.AreEqual(Now2, matched.Time);
            Assert.AreEqual(110, matched.Price);
            Assert.AreEqual(3, matched.Quantity);
            Assert.IsNotNull(matched.Fills);
            Assert.AreEqual(2, matched.Fills.Count);

            Assert.AreEqual(Sec, matched.Fills[0].Security);
            Assert.AreEqual(Now2, matched.Fills[0].Time);
            Assert.AreEqual(ClientId1, matched.Fills[0].ClientId);
            Assert.AreEqual(OrderId1, matched.Fills[0].OrderId);
            Assert.AreEqual(110, matched.Fills[0].Price);
            Assert.AreEqual(3, matched.Fills[0].Quantity);
            Assert.AreEqual(true, matched.Fills[0].IsResting);
            Assert.AreEqual(Sec, matched.Fills[0].Security);
            Assert.AreEqual(Now2, matched.Fills[0].Time);
            Assert.AreEqual(ClientId1, matched.Fills[0].ClientId);
            Assert.AreEqual(OrderId1, matched.Fills[0].OrderId);
            Assert.AreEqual(true, matched.Fills[0].IsResting);
            Assert.AreEqual(ClientId1, matched.Fills[0].Order.ClientId);
            Assert.AreEqual(OrderId1, matched.Fills[0].Order.OrderId);
            Assert.AreEqual(Sec, matched.Fills[0].Order.Security);
            Assert.AreEqual(Now1, matched.Fills[0].Order.CreatedTime);
            Assert.AreEqual(Now1, matched.Fills[0].Order.ModifiedTime);
            Assert.IsNull(matched.Fills[0].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched.Fills[0].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched.Fills[0].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched.Fills[0].Order.OrderValidity);
            Assert.AreEqual(Side.Buy, matched.Fills[0].Order.Side);
            Assert.AreEqual(110, matched.Fills[0].Order.Price);
            Assert.IsNull(matched.Fills[0].Order.StopPrice);
            Assert.AreEqual(5, matched.Fills[0].Order.Quantity);
            Assert.AreEqual(3, matched.Fills[0].Order.FilledQuantity);
            Assert.AreEqual(2, matched.Fills[0].Order.RemainingQuantity);

            Assert.AreEqual(Sec, matched.Fills[1].Security);
            Assert.AreEqual(Now2, matched.Fills[1].Time);
            Assert.AreEqual(ClientId2, matched.Fills[1].ClientId);
            Assert.AreEqual(OrderId2, matched.Fills[1].OrderId);
            Assert.AreEqual(110, matched.Fills[1].Price);
            Assert.AreEqual(3, matched.Fills[1].Quantity);
            Assert.AreEqual(false, matched.Fills[1].IsResting);
            Assert.AreEqual(ClientId2, matched.Fills[1].Order.ClientId);
            Assert.AreEqual(OrderId2, matched.Fills[1].Order.OrderId);
            Assert.AreEqual(Sec, matched.Fills[1].Order.Security);
            Assert.AreEqual(Now2, matched.Fills[1].Order.CreatedTime);
            Assert.AreEqual(Now2, matched.Fills[1].Order.ModifiedTime);
            Assert.AreEqual(Now2, matched.Fills[1].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched.Fills[1].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched.Fills[1].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched.Fills[1].Order.OrderValidity);
            Assert.AreEqual(Side.Sell, matched.Fills[1].Order.Side);
            Assert.AreEqual(100, matched.Fills[1].Order.Price);
            Assert.IsNull(matched.Fills[1].Order.StopPrice);
            Assert.AreEqual(3, matched.Fills[1].Order.Quantity);
            Assert.AreEqual(3, matched.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(0, matched.Fills[1].Order.RemainingQuantity);
        }

        [Test]
        public void CreateLimitOrder_MatchSellAgainstOrderByTime()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 110, 5);
            TimeProvider.SetCurrentTime(Now2);
            Book.CreateLimitOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Buy, 120, 5);
            TimeProvider.SetCurrentTime(Now3);

            // act
            var events = Book.CreateLimitOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Sell, 100, 3);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(2, events.Count);

            var matched = events[1] as OrdersMatched;
            Assert.IsNotNull(matched);
            Assert.AreEqual(Sec, matched.Security);
            Assert.AreEqual(Now3, matched.Time);
            Assert.AreEqual(120, matched.Price);
            Assert.AreEqual(3, matched.Quantity);
            Assert.IsNotNull(matched.Fills);
            Assert.AreEqual(2, matched.Fills.Count);

            Assert.AreEqual(Sec, matched.Fills[0].Security);
            Assert.AreEqual(Now3, matched.Fills[0].Time);
            Assert.AreEqual(ClientId2, matched.Fills[0].ClientId);
            Assert.AreEqual(OrderId2, matched.Fills[0].OrderId);
            Assert.AreEqual(120, matched.Fills[0].Price);
            Assert.AreEqual(3, matched.Fills[0].Quantity);
            Assert.AreEqual(true, matched.Fills[0].IsResting);
            Assert.AreEqual(ClientId2, matched.Fills[0].Order.ClientId);
            Assert.AreEqual(OrderId2, matched.Fills[0].Order.OrderId);
            Assert.AreEqual(Sec, matched.Fills[0].Order.Security);
            Assert.AreEqual(Now2, matched.Fills[0].Order.CreatedTime);
            Assert.AreEqual(Now2, matched.Fills[0].Order.ModifiedTime);
            Assert.IsNull(matched.Fills[0].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched.Fills[0].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched.Fills[0].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched.Fills[0].Order.OrderValidity);
            Assert.AreEqual(Side.Buy, matched.Fills[0].Order.Side);
            Assert.AreEqual(120, matched.Fills[0].Order.Price);
            Assert.IsNull(matched.Fills[0].Order.StopPrice);
            Assert.AreEqual(5, matched.Fills[0].Order.Quantity);
            Assert.AreEqual(3, matched.Fills[0].Order.FilledQuantity);
            Assert.AreEqual(2, matched.Fills[0].Order.RemainingQuantity);

            Assert.AreEqual(Sec, matched.Fills[1].Security);
            Assert.AreEqual(Now3, matched.Fills[1].Time);
            Assert.AreEqual(ClientId3, matched.Fills[1].ClientId);
            Assert.AreEqual(OrderId3, matched.Fills[1].OrderId);
            Assert.AreEqual(120, matched.Fills[1].Price);
            Assert.AreEqual(3, matched.Fills[1].Quantity);
            Assert.AreEqual(false, matched.Fills[1].IsResting);
            Assert.AreEqual(ClientId3, matched.Fills[1].Order.ClientId);
            Assert.AreEqual(OrderId3, matched.Fills[1].Order.OrderId);
            Assert.AreEqual(Sec, matched.Fills[1].Order.Security);
            Assert.AreEqual(Now3, matched.Fills[1].Order.CreatedTime);
            Assert.AreEqual(Now3, matched.Fills[1].Order.ModifiedTime);
            Assert.AreEqual(Now3, matched.Fills[1].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched.Fills[1].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched.Fills[1].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched.Fills[1].Order.OrderValidity);
            Assert.AreEqual(Side.Sell, matched.Fills[1].Order.Side);
            Assert.AreEqual(100, matched.Fills[1].Order.Price);
            Assert.IsNull(matched.Fills[1].Order.StopPrice);
            Assert.AreEqual(3, matched.Fills[1].Order.Quantity);
            Assert.AreEqual(3, matched.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(0, matched.Fills[1].Order.RemainingQuantity);
        }

        [Test]
        public void CreateLimitOrder_MatchSellAgainstOrdersByPrice()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 110, 5);
            TimeProvider.SetCurrentTime(Now2);
            Book.CreateLimitOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Buy, 120, 5);
            TimeProvider.SetCurrentTime(Now3);

            // act
            var events = Book.CreateLimitOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Sell, 100, 8);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(3, events.Count);

            var matched1 = events[1] as OrdersMatched;
            Assert.IsNotNull(matched1);
            Assert.AreEqual(Sec, matched1.Security);
            Assert.AreEqual(Now3, matched1.Time);
            Assert.AreEqual(120, matched1.Price);
            Assert.AreEqual(5, matched1.Quantity);
            Assert.IsNotNull(matched1.Fills);
            Assert.AreEqual(2, matched1.Fills.Count);

            Assert.AreEqual(Sec, matched1.Fills[0].Security);
            Assert.AreEqual(Now3, matched1.Fills[0].Time);
            Assert.AreEqual(ClientId2, matched1.Fills[0].ClientId);
            Assert.AreEqual(OrderId2, matched1.Fills[0].OrderId);
            Assert.AreEqual(120, matched1.Fills[0].Price);
            Assert.AreEqual(5, matched1.Fills[0].Quantity);
            Assert.AreEqual(true, matched1.Fills[0].IsResting);
            Assert.AreEqual(ClientId2, matched1.Fills[0].Order.ClientId);
            Assert.AreEqual(OrderId2, matched1.Fills[0].Order.OrderId);
            Assert.AreEqual(Sec, matched1.Fills[0].Order.Security);
            Assert.AreEqual(Now2, matched1.Fills[0].Order.CreatedTime);
            Assert.AreEqual(Now2, matched1.Fills[0].Order.ModifiedTime);
            Assert.AreEqual(Now3, matched1.Fills[0].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched1.Fills[0].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched1.Fills[0].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched1.Fills[0].Order.OrderValidity);
            Assert.AreEqual(Side.Buy, matched1.Fills[0].Order.Side);
            Assert.AreEqual(120, matched1.Fills[0].Order.Price);
            Assert.IsNull(matched1.Fills[0].Order.StopPrice);
            Assert.AreEqual(5, matched1.Fills[0].Order.Quantity);
            Assert.AreEqual(5, matched1.Fills[0].Order.FilledQuantity);
            Assert.AreEqual(0, matched1.Fills[0].Order.RemainingQuantity);

            Assert.AreEqual(Sec, matched1.Fills[1].Security);
            Assert.AreEqual(Now3, matched1.Fills[1].Time);
            Assert.AreEqual(ClientId3, matched1.Fills[1].ClientId);
            Assert.AreEqual(OrderId3, matched1.Fills[1].OrderId);
            Assert.AreEqual(120, matched1.Fills[1].Price);
            Assert.AreEqual(5, matched1.Fills[1].Quantity);
            Assert.AreEqual(false, matched1.Fills[1].IsResting);
            Assert.AreEqual(ClientId3, matched1.Fills[1].Order.ClientId);
            Assert.AreEqual(OrderId3, matched1.Fills[1].Order.OrderId);
            Assert.AreEqual(Sec, matched1.Fills[1].Order.Security);
            Assert.AreEqual(Now3, matched1.Fills[1].Order.CreatedTime);
            Assert.AreEqual(Now3, matched1.Fills[1].Order.ModifiedTime);
            Assert.IsNull(matched1.Fills[1].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched1.Fills[1].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched1.Fills[1].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched1.Fills[1].Order.OrderValidity);
            Assert.AreEqual(Side.Sell, matched1.Fills[1].Order.Side);
            Assert.AreEqual(100, matched1.Fills[1].Order.Price);
            Assert.IsNull(matched1.Fills[1].Order.StopPrice);
            Assert.AreEqual(8, matched1.Fills[1].Order.Quantity);
            Assert.AreEqual(5, matched1.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(3, matched1.Fills[1].Order.RemainingQuantity);

            var matched2 = events[2] as OrdersMatched;
            Assert.IsNotNull(matched2);
            Assert.AreEqual(Sec, matched2.Security);
            Assert.AreEqual(Now3, matched2.Time);
            Assert.AreEqual(110, matched2.Price);
            Assert.AreEqual(3, matched2.Quantity);
            Assert.IsNotNull(matched2.Fills);
            Assert.AreEqual(2, matched2.Fills.Count);

            Assert.AreEqual(Sec, matched2.Fills[0].Security);
            Assert.AreEqual(Now3, matched2.Fills[0].Time);
            Assert.AreEqual(ClientId1, matched2.Fills[0].ClientId);
            Assert.AreEqual(OrderId1, matched2.Fills[0].OrderId);
            Assert.AreEqual(110, matched2.Fills[0].Price);
            Assert.AreEqual(3, matched2.Fills[0].Quantity);
            Assert.AreEqual(true, matched2.Fills[0].IsResting);
            Assert.AreEqual(ClientId1, matched2.Fills[0].Order.ClientId);
            Assert.AreEqual(OrderId1, matched2.Fills[0].Order.OrderId);
            Assert.AreEqual(Sec, matched2.Fills[0].Order.Security);
            Assert.AreEqual(Now1, matched2.Fills[0].Order.CreatedTime);
            Assert.AreEqual(Now1, matched2.Fills[0].Order.ModifiedTime);
            Assert.IsNull(matched2.Fills[0].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched2.Fills[0].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched2.Fills[0].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched2.Fills[0].Order.OrderValidity);
            Assert.AreEqual(Side.Buy, matched2.Fills[0].Order.Side);
            Assert.AreEqual(110, matched2.Fills[0].Order.Price);
            Assert.IsNull(matched2.Fills[0].Order.StopPrice);
            Assert.AreEqual(5, matched2.Fills[0].Order.Quantity);
            Assert.AreEqual(3, matched2.Fills[0].Order.FilledQuantity);
            Assert.AreEqual(2, matched2.Fills[0].Order.RemainingQuantity);

            Assert.AreEqual(Sec, matched2.Fills[1].Security);
            Assert.AreEqual(Now3, matched2.Fills[1].Time);
            Assert.AreEqual(ClientId3, matched2.Fills[1].ClientId);
            Assert.AreEqual(OrderId3, matched2.Fills[1].OrderId);
            Assert.AreEqual(110, matched2.Fills[1].Price);
            Assert.AreEqual(3, matched2.Fills[1].Quantity);
            Assert.AreEqual(false, matched2.Fills[1].IsResting);
            Assert.AreEqual(ClientId3, matched2.Fills[1].Order.ClientId);
            Assert.AreEqual(OrderId3, matched2.Fills[1].Order.OrderId);
            Assert.AreEqual(Sec, matched2.Fills[1].Order.Security);
            Assert.AreEqual(Now3, matched2.Fills[1].Order.CreatedTime);
            Assert.AreEqual(Now3, matched2.Fills[1].Order.ModifiedTime);
            Assert.AreEqual(Now3, matched2.Fills[1].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched2.Fills[1].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched2.Fills[1].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched2.Fills[1].Order.OrderValidity);
            Assert.AreEqual(Side.Sell, matched2.Fills[1].Order.Side);
            Assert.AreEqual(100, matched2.Fills[1].Order.Price);
            Assert.IsNull(matched2.Fills[1].Order.StopPrice);
            Assert.AreEqual(8, matched2.Fills[1].Order.Quantity);
            Assert.AreEqual(8, matched2.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(0, matched2.Fills[1].Order.RemainingQuantity);
        }

        [Test]
        public void CreateLimitOrder_MatchSellAgainstOrderAtSamePriceByTime()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 110, 5);
            TimeProvider.SetCurrentTime(Now2);
            Book.CreateLimitOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Buy, 110, 5);
            TimeProvider.SetCurrentTime(Now3);

            // act
            var events = Book.CreateLimitOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Sell, 100, 3);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(2, events.Count);

            var matched = events[1] as OrdersMatched;
            Assert.IsNotNull(matched);
            Assert.AreEqual(Sec, matched.Security);
            Assert.AreEqual(Now3, matched.Time);
            Assert.AreEqual(110, matched.Price);
            Assert.AreEqual(3, matched.Quantity);
            Assert.IsNotNull(matched.Fills);
            Assert.AreEqual(2, matched.Fills.Count);

            Assert.AreEqual(Sec, matched.Fills[0].Security);
            Assert.AreEqual(Now3, matched.Fills[0].Time);
            Assert.AreEqual(ClientId1, matched.Fills[0].ClientId);
            Assert.AreEqual(OrderId1, matched.Fills[0].OrderId);
            Assert.AreEqual(110, matched.Fills[0].Price);
            Assert.AreEqual(3, matched.Fills[0].Quantity);
            Assert.AreEqual(true, matched.Fills[0].IsResting);
            Assert.AreEqual(Sec, matched.Fills[0].Security);
            Assert.AreEqual(Now3, matched.Fills[0].Time);
            Assert.AreEqual(ClientId1, matched.Fills[0].ClientId);
            Assert.AreEqual(OrderId1, matched.Fills[0].OrderId);
            Assert.AreEqual(true, matched.Fills[0].IsResting);
            Assert.AreEqual(ClientId1, matched.Fills[0].Order.ClientId);
            Assert.AreEqual(OrderId1, matched.Fills[0].Order.OrderId);
            Assert.AreEqual(Sec, matched.Fills[0].Order.Security);
            Assert.AreEqual(Now1, matched.Fills[0].Order.CreatedTime);
            Assert.AreEqual(Now1, matched.Fills[0].Order.ModifiedTime);
            Assert.IsNull(matched.Fills[0].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched.Fills[0].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched.Fills[0].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched.Fills[0].Order.OrderValidity);
            Assert.AreEqual(Side.Buy, matched.Fills[0].Order.Side);
            Assert.AreEqual(110, matched.Fills[0].Order.Price);
            Assert.IsNull(matched.Fills[0].Order.StopPrice);
            Assert.AreEqual(5, matched.Fills[0].Order.Quantity);
            Assert.AreEqual(3, matched.Fills[0].Order.FilledQuantity);
            Assert.AreEqual(2, matched.Fills[0].Order.RemainingQuantity);

            Assert.AreEqual(Sec, matched.Fills[1].Security);
            Assert.AreEqual(Now3, matched.Fills[1].Time);
            Assert.AreEqual(ClientId3, matched.Fills[1].ClientId);
            Assert.AreEqual(OrderId3, matched.Fills[1].OrderId);
            Assert.AreEqual(110, matched.Fills[1].Price);
            Assert.AreEqual(3, matched.Fills[1].Quantity);
            Assert.AreEqual(false, matched.Fills[1].IsResting);
            Assert.AreEqual(ClientId3, matched.Fills[1].Order.ClientId);
            Assert.AreEqual(OrderId3, matched.Fills[1].Order.OrderId);
            Assert.AreEqual(Sec, matched.Fills[1].Order.Security);
            Assert.AreEqual(Now3, matched.Fills[1].Order.CreatedTime);
            Assert.AreEqual(Now3, matched.Fills[1].Order.ModifiedTime);
            Assert.AreEqual(Now3, matched.Fills[1].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched.Fills[1].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched.Fills[1].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched.Fills[1].Order.OrderValidity);
            Assert.AreEqual(Side.Sell, matched.Fills[1].Order.Side);
            Assert.AreEqual(100, matched.Fills[1].Order.Price);
            Assert.IsNull(matched.Fills[1].Order.StopPrice);
            Assert.AreEqual(3, matched.Fills[1].Order.Quantity);
            Assert.AreEqual(3, matched.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(0, matched.Fills[1].Order.RemainingQuantity);
        }

        [Test]
        public void CreateLimitOrder_MatchSellAgainstOrdersAtSamePriceByTime()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 110, 5);
            TimeProvider.SetCurrentTime(Now2);
            Book.CreateLimitOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Buy, 110, 5);
            TimeProvider.SetCurrentTime(Now3);

            // act
            var events = Book.CreateLimitOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Sell, 100, 8);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(3, events.Count);

            var matched1 = events[1] as OrdersMatched;
            Assert.IsNotNull(matched1);
            Assert.AreEqual(Sec, matched1.Security);
            Assert.AreEqual(Now3, matched1.Time);
            Assert.AreEqual(110, matched1.Price);
            Assert.AreEqual(5, matched1.Quantity);
            Assert.IsNotNull(matched1.Fills);
            Assert.AreEqual(2, matched1.Fills.Count);

            Assert.AreEqual(Sec, matched1.Fills[0].Security);
            Assert.AreEqual(Now3, matched1.Fills[0].Time);
            Assert.AreEqual(ClientId1, matched1.Fills[0].ClientId);
            Assert.AreEqual(OrderId1, matched1.Fills[0].OrderId);
            Assert.AreEqual(110, matched1.Fills[0].Price);
            Assert.AreEqual(5, matched1.Fills[0].Quantity);
            Assert.AreEqual(true, matched1.Fills[0].IsResting);
            Assert.AreEqual(ClientId1, matched1.Fills[0].Order.ClientId);
            Assert.AreEqual(OrderId1, matched1.Fills[0].Order.OrderId);
            Assert.AreEqual(Sec, matched1.Fills[0].Order.Security);
            Assert.AreEqual(Now1, matched1.Fills[0].Order.CreatedTime);
            Assert.AreEqual(Now1, matched1.Fills[0].Order.ModifiedTime);
            Assert.AreEqual(Now3, matched1.Fills[0].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched1.Fills[0].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched1.Fills[0].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched1.Fills[0].Order.OrderValidity);
            Assert.AreEqual(Side.Buy, matched1.Fills[0].Order.Side);
            Assert.AreEqual(110, matched1.Fills[0].Order.Price);
            Assert.IsNull(matched1.Fills[0].Order.StopPrice);
            Assert.AreEqual(5, matched1.Fills[0].Order.Quantity);
            Assert.AreEqual(5, matched1.Fills[0].Order.FilledQuantity);
            Assert.AreEqual(0, matched1.Fills[0].Order.RemainingQuantity);

            Assert.AreEqual(Sec, matched1.Fills[1].Security);
            Assert.AreEqual(Now3, matched1.Fills[1].Time);
            Assert.AreEqual(ClientId3, matched1.Fills[1].ClientId);
            Assert.AreEqual(OrderId3, matched1.Fills[1].OrderId);
            Assert.AreEqual(110, matched1.Fills[1].Price);
            Assert.AreEqual(5, matched1.Fills[1].Quantity);
            Assert.AreEqual(false, matched1.Fills[1].IsResting);
            Assert.AreEqual(ClientId3, matched1.Fills[1].Order.ClientId);
            Assert.AreEqual(OrderId3, matched1.Fills[1].Order.OrderId);
            Assert.AreEqual(Sec, matched1.Fills[1].Order.Security);
            Assert.AreEqual(Now3, matched1.Fills[1].Order.CreatedTime);
            Assert.AreEqual(Now3, matched1.Fills[1].Order.ModifiedTime);
            Assert.IsNull(matched1.Fills[1].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched1.Fills[1].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched1.Fills[1].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched1.Fills[1].Order.OrderValidity);
            Assert.AreEqual(Side.Sell, matched1.Fills[1].Order.Side);
            Assert.AreEqual(100, matched1.Fills[1].Order.Price);
            Assert.IsNull(matched1.Fills[1].Order.StopPrice);
            Assert.AreEqual(8, matched1.Fills[1].Order.Quantity);
            Assert.AreEqual(5, matched1.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(3, matched1.Fills[1].Order.RemainingQuantity);

            var matched2 = events[2] as OrdersMatched;
            Assert.IsNotNull(matched2);
            Assert.AreEqual(Sec, matched2.Security);
            Assert.AreEqual(Now3, matched2.Time);
            Assert.AreEqual(110, matched2.Price);
            Assert.AreEqual(3, matched2.Quantity);
            Assert.IsNotNull(matched2.Fills);
            Assert.AreEqual(2, matched2.Fills.Count);

            Assert.AreEqual(Sec, matched2.Fills[0].Security);
            Assert.AreEqual(Now3, matched2.Fills[0].Time);
            Assert.AreEqual(ClientId2, matched2.Fills[0].ClientId);
            Assert.AreEqual(OrderId2, matched2.Fills[0].OrderId);
            Assert.AreEqual(110, matched2.Fills[0].Price);
            Assert.AreEqual(3, matched2.Fills[0].Quantity);
            Assert.AreEqual(true, matched2.Fills[0].IsResting);
            Assert.AreEqual(ClientId2, matched2.Fills[0].Order.ClientId);
            Assert.AreEqual(OrderId2, matched2.Fills[0].Order.OrderId);
            Assert.AreEqual(Sec, matched2.Fills[0].Order.Security);
            Assert.AreEqual(Now2, matched2.Fills[0].Order.CreatedTime);
            Assert.AreEqual(Now2, matched2.Fills[0].Order.ModifiedTime);
            Assert.IsNull(matched2.Fills[0].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched2.Fills[0].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched2.Fills[0].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched2.Fills[0].Order.OrderValidity);
            Assert.AreEqual(Side.Buy, matched2.Fills[0].Order.Side);
            Assert.AreEqual(110, matched2.Fills[0].Order.Price);
            Assert.IsNull(matched2.Fills[0].Order.StopPrice);
            Assert.AreEqual(5, matched2.Fills[0].Order.Quantity);
            Assert.AreEqual(3, matched2.Fills[0].Order.FilledQuantity);
            Assert.AreEqual(2, matched2.Fills[0].Order.RemainingQuantity);

            Assert.AreEqual(Sec, matched2.Fills[1].Security);
            Assert.AreEqual(Now3, matched2.Fills[1].Time);
            Assert.AreEqual(ClientId3, matched2.Fills[1].ClientId);
            Assert.AreEqual(OrderId3, matched2.Fills[1].OrderId);
            Assert.AreEqual(110, matched2.Fills[1].Price);
            Assert.AreEqual(3, matched2.Fills[1].Quantity);
            Assert.AreEqual(false, matched2.Fills[1].IsResting);
            Assert.AreEqual(ClientId3, matched2.Fills[1].Order.ClientId);
            Assert.AreEqual(OrderId3, matched2.Fills[1].Order.OrderId);
            Assert.AreEqual(Sec, matched2.Fills[1].Order.Security);
            Assert.AreEqual(Now3, matched2.Fills[1].Order.CreatedTime);
            Assert.AreEqual(Now3, matched2.Fills[1].Order.ModifiedTime);
            Assert.AreEqual(Now3, matched2.Fills[1].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched2.Fills[1].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched2.Fills[1].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched2.Fills[1].Order.OrderValidity);
            Assert.AreEqual(Side.Sell, matched2.Fills[1].Order.Side);
            Assert.AreEqual(100, matched2.Fills[1].Order.Price);
            Assert.IsNull(matched2.Fills[1].Order.StopPrice);
            Assert.AreEqual(8, matched2.Fills[1].Order.Quantity);
            Assert.AreEqual(8, matched2.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(0, matched2.Fills[1].Order.RemainingQuantity);
        }

        [Test]
        public void CreateLimitOrder_MatchBuyAgainstOrderByTime()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Sell, 90, 5);
            TimeProvider.SetCurrentTime(Now2);
            Book.CreateLimitOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 80, 5);
            TimeProvider.SetCurrentTime(Now3);

            // act
            var events = Book.CreateLimitOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Buy, 100, 3);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(2, events.Count);

            var matched = events[1] as OrdersMatched;
            Assert.IsNotNull(matched);
            Assert.AreEqual(Sec, matched.Security);
            Assert.AreEqual(Now3, matched.Time);
            Assert.AreEqual(80, matched.Price);
            Assert.AreEqual(3, matched.Quantity);
            Assert.IsNotNull(matched.Fills);
            Assert.AreEqual(2, matched.Fills.Count);

            Assert.AreEqual(Sec, matched.Fills[0].Security);
            Assert.AreEqual(Now3, matched.Fills[0].Time);
            Assert.AreEqual(ClientId2, matched.Fills[0].ClientId);
            Assert.AreEqual(OrderId2, matched.Fills[0].OrderId);
            Assert.AreEqual(80, matched.Fills[0].Price);
            Assert.AreEqual(3, matched.Fills[0].Quantity);
            Assert.AreEqual(true, matched.Fills[0].IsResting);
            Assert.AreEqual(ClientId2, matched.Fills[0].Order.ClientId);
            Assert.AreEqual(OrderId2, matched.Fills[0].Order.OrderId);
            Assert.AreEqual(Sec, matched.Fills[0].Order.Security);
            Assert.AreEqual(Now2, matched.Fills[0].Order.CreatedTime);
            Assert.AreEqual(Now2, matched.Fills[0].Order.ModifiedTime);
            Assert.IsNull(matched.Fills[0].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched.Fills[0].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched.Fills[0].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched.Fills[0].Order.OrderValidity);
            Assert.AreEqual(Side.Sell, matched.Fills[0].Order.Side);
            Assert.AreEqual(80, matched.Fills[0].Order.Price);
            Assert.IsNull(matched.Fills[0].Order.StopPrice);
            Assert.AreEqual(5, matched.Fills[0].Order.Quantity);
            Assert.AreEqual(3, matched.Fills[0].Order.FilledQuantity);
            Assert.AreEqual(2, matched.Fills[0].Order.RemainingQuantity);

            Assert.AreEqual(Sec, matched.Fills[1].Security);
            Assert.AreEqual(Now3, matched.Fills[1].Time);
            Assert.AreEqual(ClientId3, matched.Fills[1].ClientId);
            Assert.AreEqual(OrderId3, matched.Fills[1].OrderId);
            Assert.AreEqual(80, matched.Fills[1].Price);
            Assert.AreEqual(3, matched.Fills[1].Quantity);
            Assert.AreEqual(false, matched.Fills[1].IsResting);
            Assert.AreEqual(ClientId3, matched.Fills[1].Order.ClientId);
            Assert.AreEqual(OrderId3, matched.Fills[1].Order.OrderId);
            Assert.AreEqual(Sec, matched.Fills[1].Order.Security);
            Assert.AreEqual(Now3, matched.Fills[1].Order.CreatedTime);
            Assert.AreEqual(Now3, matched.Fills[1].Order.ModifiedTime);
            Assert.AreEqual(Now3, matched.Fills[1].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched.Fills[1].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched.Fills[1].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched.Fills[1].Order.OrderValidity);
            Assert.AreEqual(Side.Buy, matched.Fills[1].Order.Side);
            Assert.AreEqual(100, matched.Fills[1].Order.Price);
            Assert.IsNull(matched.Fills[1].Order.StopPrice);
            Assert.AreEqual(3, matched.Fills[1].Order.Quantity);
            Assert.AreEqual(3, matched.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(0, matched.Fills[1].Order.RemainingQuantity);
        }

        [Test]
        public void CreateLimitOrder_MatchBuyAgainstOrdersByPrice()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Sell, 90, 5);
            TimeProvider.SetCurrentTime(Now2);
            Book.CreateLimitOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 80, 5);
            TimeProvider.SetCurrentTime(Now3);

            // act
            var events = Book.CreateLimitOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Buy, 100, 8);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(3, events.Count);

            var matched1 = events[1] as OrdersMatched;
            Assert.IsNotNull(matched1);
            Assert.AreEqual(Sec, matched1.Security);
            Assert.AreEqual(Now3, matched1.Time);
            Assert.AreEqual(80, matched1.Price);
            Assert.AreEqual(5, matched1.Quantity);
            Assert.IsNotNull(matched1.Fills);
            Assert.AreEqual(2, matched1.Fills.Count);

            Assert.AreEqual(Sec, matched1.Fills[0].Security);
            Assert.AreEqual(Now3, matched1.Fills[0].Time);
            Assert.AreEqual(ClientId2, matched1.Fills[0].ClientId);
            Assert.AreEqual(OrderId2, matched1.Fills[0].OrderId);
            Assert.AreEqual(80, matched1.Fills[0].Price);
            Assert.AreEqual(5, matched1.Fills[0].Quantity);
            Assert.AreEqual(true, matched1.Fills[0].IsResting);
            Assert.AreEqual(ClientId2, matched1.Fills[0].Order.ClientId);
            Assert.AreEqual(OrderId2, matched1.Fills[0].Order.OrderId);
            Assert.AreEqual(Sec, matched1.Fills[0].Order.Security);
            Assert.AreEqual(Now2, matched1.Fills[0].Order.CreatedTime);
            Assert.AreEqual(Now2, matched1.Fills[0].Order.ModifiedTime);
            Assert.AreEqual(Now3, matched1.Fills[0].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched1.Fills[0].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched1.Fills[0].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched1.Fills[0].Order.OrderValidity);
            Assert.AreEqual(Side.Sell, matched1.Fills[0].Order.Side);
            Assert.AreEqual(80, matched1.Fills[0].Order.Price);
            Assert.IsNull(matched1.Fills[0].Order.StopPrice);
            Assert.AreEqual(5, matched1.Fills[0].Order.Quantity);
            Assert.AreEqual(5, matched1.Fills[0].Order.FilledQuantity);
            Assert.AreEqual(0, matched1.Fills[0].Order.RemainingQuantity);

            Assert.AreEqual(Sec, matched1.Fills[1].Security);
            Assert.AreEqual(Now3, matched1.Fills[1].Time);
            Assert.AreEqual(ClientId3, matched1.Fills[1].ClientId);
            Assert.AreEqual(OrderId3, matched1.Fills[1].OrderId);
            Assert.AreEqual(80, matched1.Fills[1].Price);
            Assert.AreEqual(5, matched1.Fills[1].Quantity);
            Assert.AreEqual(false, matched1.Fills[1].IsResting);
            Assert.AreEqual(ClientId3, matched1.Fills[1].Order.ClientId);
            Assert.AreEqual(OrderId3, matched1.Fills[1].Order.OrderId);
            Assert.AreEqual(Sec, matched1.Fills[1].Order.Security);
            Assert.AreEqual(Now3, matched1.Fills[1].Order.CreatedTime);
            Assert.AreEqual(Now3, matched1.Fills[1].Order.ModifiedTime);
            Assert.IsNull(matched1.Fills[1].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched1.Fills[1].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched1.Fills[1].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched1.Fills[1].Order.OrderValidity);
            Assert.AreEqual(Side.Buy, matched1.Fills[1].Order.Side);
            Assert.AreEqual(100, matched1.Fills[1].Order.Price);
            Assert.IsNull(matched1.Fills[1].Order.StopPrice);
            Assert.AreEqual(8, matched1.Fills[1].Order.Quantity);
            Assert.AreEqual(5, matched1.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(3, matched1.Fills[1].Order.RemainingQuantity);

            var matched2 = events[2] as OrdersMatched;
            Assert.IsNotNull(matched2);
            Assert.AreEqual(Sec, matched2.Security);
            Assert.AreEqual(Now3, matched2.Time);
            Assert.AreEqual(90, matched2.Price);
            Assert.AreEqual(3, matched2.Quantity);
            Assert.IsNotNull(matched2.Fills);
            Assert.AreEqual(2, matched2.Fills.Count);

            Assert.AreEqual(Sec, matched2.Fills[0].Security);
            Assert.AreEqual(Now3, matched2.Fills[0].Time);
            Assert.AreEqual(ClientId1, matched2.Fills[0].ClientId);
            Assert.AreEqual(OrderId1, matched2.Fills[0].OrderId);
            Assert.AreEqual(90, matched2.Fills[0].Price);
            Assert.AreEqual(3, matched2.Fills[0].Quantity);
            Assert.AreEqual(true, matched2.Fills[0].IsResting);
            Assert.AreEqual(ClientId1, matched2.Fills[0].Order.ClientId);
            Assert.AreEqual(OrderId1, matched2.Fills[0].Order.OrderId);
            Assert.AreEqual(Sec, matched2.Fills[0].Order.Security);
            Assert.AreEqual(Now1, matched2.Fills[0].Order.CreatedTime);
            Assert.AreEqual(Now1, matched2.Fills[0].Order.ModifiedTime);
            Assert.IsNull(matched2.Fills[0].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched2.Fills[0].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched2.Fills[0].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched2.Fills[0].Order.OrderValidity);
            Assert.AreEqual(Side.Sell, matched2.Fills[0].Order.Side);
            Assert.AreEqual(90, matched2.Fills[0].Order.Price);
            Assert.IsNull(matched2.Fills[0].Order.StopPrice);
            Assert.AreEqual(5, matched2.Fills[0].Order.Quantity);
            Assert.AreEqual(3, matched2.Fills[0].Order.FilledQuantity);
            Assert.AreEqual(2, matched2.Fills[0].Order.RemainingQuantity);

            Assert.AreEqual(Sec, matched2.Fills[1].Security);
            Assert.AreEqual(Now3, matched2.Fills[1].Time);
            Assert.AreEqual(ClientId3, matched2.Fills[1].ClientId);
            Assert.AreEqual(OrderId3, matched2.Fills[1].OrderId);
            Assert.AreEqual(90, matched2.Fills[1].Price);
            Assert.AreEqual(3, matched2.Fills[1].Quantity);
            Assert.AreEqual(false, matched2.Fills[1].IsResting);
            Assert.AreEqual(ClientId3, matched2.Fills[1].Order.ClientId);
            Assert.AreEqual(OrderId3, matched2.Fills[1].Order.OrderId);
            Assert.AreEqual(Sec, matched2.Fills[1].Order.Security);
            Assert.AreEqual(Now3, matched2.Fills[1].Order.CreatedTime);
            Assert.AreEqual(Now3, matched2.Fills[1].Order.ModifiedTime);
            Assert.AreEqual(Now3, matched2.Fills[1].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched2.Fills[1].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched2.Fills[1].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched2.Fills[1].Order.OrderValidity);
            Assert.AreEqual(Side.Buy, matched2.Fills[1].Order.Side);
            Assert.AreEqual(100, matched2.Fills[1].Order.Price);
            Assert.IsNull(matched2.Fills[1].Order.StopPrice);
            Assert.AreEqual(8, matched2.Fills[1].Order.Quantity);
            Assert.AreEqual(8, matched2.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(0, matched2.Fills[1].Order.RemainingQuantity);
        }

        [Test]
        public void CreateLimitOrder_MatchBuyAgainstOrderAtSamePriceByTime()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Sell, 90, 5);
            TimeProvider.SetCurrentTime(Now2);
            Book.CreateLimitOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 90, 5);
            TimeProvider.SetCurrentTime(Now3);

            // act
            var events = Book.CreateLimitOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Buy, 100, 3);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(2, events.Count);

            var matched = events[1] as OrdersMatched;
            Assert.IsNotNull(matched);
            Assert.AreEqual(Sec, matched.Security);
            Assert.AreEqual(Now3, matched.Time);
            Assert.AreEqual(90, matched.Price);
            Assert.AreEqual(3, matched.Quantity);
            Assert.IsNotNull(matched.Fills);
            Assert.AreEqual(2, matched.Fills.Count);

            Assert.AreEqual(Sec, matched.Fills[0].Security);
            Assert.AreEqual(Now3, matched.Fills[0].Time);
            Assert.AreEqual(ClientId1, matched.Fills[0].ClientId);
            Assert.AreEqual(OrderId1, matched.Fills[0].OrderId);
            Assert.AreEqual(90, matched.Fills[0].Price);
            Assert.AreEqual(3, matched.Fills[0].Quantity);
            Assert.AreEqual(true, matched.Fills[0].IsResting);
            Assert.AreEqual(Sec, matched.Fills[0].Security);
            Assert.AreEqual(Now3, matched.Fills[0].Time);
            Assert.AreEqual(ClientId1, matched.Fills[0].ClientId);
            Assert.AreEqual(OrderId1, matched.Fills[0].OrderId);
            Assert.AreEqual(true, matched.Fills[0].IsResting);
            Assert.AreEqual(ClientId1, matched.Fills[0].Order.ClientId);
            Assert.AreEqual(OrderId1, matched.Fills[0].Order.OrderId);
            Assert.AreEqual(Sec, matched.Fills[0].Order.Security);
            Assert.AreEqual(Now1, matched.Fills[0].Order.CreatedTime);
            Assert.AreEqual(Now1, matched.Fills[0].Order.ModifiedTime);
            Assert.IsNull(matched.Fills[0].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched.Fills[0].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched.Fills[0].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched.Fills[0].Order.OrderValidity);
            Assert.AreEqual(Side.Sell, matched.Fills[0].Order.Side);
            Assert.AreEqual(90, matched.Fills[0].Order.Price);
            Assert.IsNull(matched.Fills[0].Order.StopPrice);
            Assert.AreEqual(5, matched.Fills[0].Order.Quantity);
            Assert.AreEqual(3, matched.Fills[0].Order.FilledQuantity);
            Assert.AreEqual(2, matched.Fills[0].Order.RemainingQuantity);

            Assert.AreEqual(Sec, matched.Fills[1].Security);
            Assert.AreEqual(Now3, matched.Fills[1].Time);
            Assert.AreEqual(ClientId3, matched.Fills[1].ClientId);
            Assert.AreEqual(OrderId3, matched.Fills[1].OrderId);
            Assert.AreEqual(90, matched.Fills[1].Price);
            Assert.AreEqual(3, matched.Fills[1].Quantity);
            Assert.AreEqual(false, matched.Fills[1].IsResting);
            Assert.AreEqual(ClientId3, matched.Fills[1].Order.ClientId);
            Assert.AreEqual(OrderId3, matched.Fills[1].Order.OrderId);
            Assert.AreEqual(Sec, matched.Fills[1].Order.Security);
            Assert.AreEqual(Now3, matched.Fills[1].Order.CreatedTime);
            Assert.AreEqual(Now3, matched.Fills[1].Order.ModifiedTime);
            Assert.AreEqual(Now3, matched.Fills[1].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched.Fills[1].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched.Fills[1].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched.Fills[1].Order.OrderValidity);
            Assert.AreEqual(Side.Buy, matched.Fills[1].Order.Side);
            Assert.AreEqual(100, matched.Fills[1].Order.Price);
            Assert.IsNull(matched.Fills[1].Order.StopPrice);
            Assert.AreEqual(3, matched.Fills[1].Order.Quantity);
            Assert.AreEqual(3, matched.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(0, matched.Fills[1].Order.RemainingQuantity);
        }

        [Test]
        public void CreateLimitOrder_MatchBuyAgainstOrdersAtSamePriceByTime()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Sell, 90, 5);
            TimeProvider.SetCurrentTime(Now2);
            Book.CreateLimitOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 90, 5);
            TimeProvider.SetCurrentTime(Now3);

            // act
            var events = Book.CreateLimitOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Buy, 100, 8);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(3, events.Count);

            var matched1 = events[1] as OrdersMatched;
            Assert.IsNotNull(matched1);
            Assert.AreEqual(Sec, matched1.Security);
            Assert.AreEqual(Now3, matched1.Time);
            Assert.AreEqual(90, matched1.Price);
            Assert.AreEqual(5, matched1.Quantity);
            Assert.IsNotNull(matched1.Fills);
            Assert.AreEqual(2, matched1.Fills.Count);

            Assert.AreEqual(Sec, matched1.Fills[0].Security);
            Assert.AreEqual(Now3, matched1.Fills[0].Time);
            Assert.AreEqual(ClientId1, matched1.Fills[0].ClientId);
            Assert.AreEqual(OrderId1, matched1.Fills[0].OrderId);
            Assert.AreEqual(90, matched1.Fills[0].Price);
            Assert.AreEqual(5, matched1.Fills[0].Quantity);
            Assert.AreEqual(true, matched1.Fills[0].IsResting);
            Assert.AreEqual(ClientId1, matched1.Fills[0].Order.ClientId);
            Assert.AreEqual(OrderId1, matched1.Fills[0].Order.OrderId);
            Assert.AreEqual(Sec, matched1.Fills[0].Order.Security);
            Assert.AreEqual(Now1, matched1.Fills[0].Order.CreatedTime);
            Assert.AreEqual(Now1, matched1.Fills[0].Order.ModifiedTime);
            Assert.AreEqual(Now3, matched1.Fills[0].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched1.Fills[0].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched1.Fills[0].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched1.Fills[0].Order.OrderValidity);
            Assert.AreEqual(Side.Sell, matched1.Fills[0].Order.Side);
            Assert.AreEqual(90, matched1.Fills[0].Order.Price);
            Assert.IsNull(matched1.Fills[0].Order.StopPrice);
            Assert.AreEqual(5, matched1.Fills[0].Order.Quantity);
            Assert.AreEqual(5, matched1.Fills[0].Order.FilledQuantity);
            Assert.AreEqual(0, matched1.Fills[0].Order.RemainingQuantity);

            Assert.AreEqual(Sec, matched1.Fills[1].Security);
            Assert.AreEqual(Now3, matched1.Fills[1].Time);
            Assert.AreEqual(ClientId3, matched1.Fills[1].ClientId);
            Assert.AreEqual(OrderId3, matched1.Fills[1].OrderId);
            Assert.AreEqual(90, matched1.Fills[1].Price);
            Assert.AreEqual(5, matched1.Fills[1].Quantity);
            Assert.AreEqual(false, matched1.Fills[1].IsResting);
            Assert.AreEqual(ClientId3, matched1.Fills[1].Order.ClientId);
            Assert.AreEqual(OrderId3, matched1.Fills[1].Order.OrderId);
            Assert.AreEqual(Sec, matched1.Fills[1].Order.Security);
            Assert.AreEqual(Now3, matched1.Fills[1].Order.CreatedTime);
            Assert.AreEqual(Now3, matched1.Fills[1].Order.ModifiedTime);
            Assert.IsNull(matched1.Fills[1].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched1.Fills[1].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched1.Fills[1].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched1.Fills[1].Order.OrderValidity);
            Assert.AreEqual(Side.Buy, matched1.Fills[1].Order.Side);
            Assert.AreEqual(100, matched1.Fills[1].Order.Price);
            Assert.IsNull(matched1.Fills[1].Order.StopPrice);
            Assert.AreEqual(8, matched1.Fills[1].Order.Quantity);
            Assert.AreEqual(5, matched1.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(3, matched1.Fills[1].Order.RemainingQuantity);

            var matched2 = events[2] as OrdersMatched;
            Assert.IsNotNull(matched2);
            Assert.AreEqual(Sec, matched2.Security);
            Assert.AreEqual(Now3, matched2.Time);
            Assert.AreEqual(90, matched2.Price);
            Assert.AreEqual(3, matched2.Quantity);
            Assert.IsNotNull(matched2.Fills);
            Assert.AreEqual(2, matched2.Fills.Count);

            Assert.AreEqual(Sec, matched2.Fills[0].Security);
            Assert.AreEqual(Now3, matched2.Fills[0].Time);
            Assert.AreEqual(ClientId2, matched2.Fills[0].ClientId);
            Assert.AreEqual(OrderId2, matched2.Fills[0].OrderId);
            Assert.AreEqual(90, matched2.Fills[0].Price);
            Assert.AreEqual(3, matched2.Fills[0].Quantity);
            Assert.AreEqual(true, matched2.Fills[0].IsResting);
            Assert.AreEqual(ClientId2, matched2.Fills[0].Order.ClientId);
            Assert.AreEqual(OrderId2, matched2.Fills[0].Order.OrderId);
            Assert.AreEqual(Sec, matched2.Fills[0].Order.Security);
            Assert.AreEqual(Now2, matched2.Fills[0].Order.CreatedTime);
            Assert.AreEqual(Now2, matched2.Fills[0].Order.ModifiedTime);
            Assert.IsNull(matched2.Fills[0].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched2.Fills[0].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched2.Fills[0].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched2.Fills[0].Order.OrderValidity);
            Assert.AreEqual(Side.Sell, matched2.Fills[0].Order.Side);
            Assert.AreEqual(90, matched2.Fills[0].Order.Price);
            Assert.IsNull(matched2.Fills[0].Order.StopPrice);
            Assert.AreEqual(5, matched2.Fills[0].Order.Quantity);
            Assert.AreEqual(3, matched2.Fills[0].Order.FilledQuantity);
            Assert.AreEqual(2, matched2.Fills[0].Order.RemainingQuantity);

            Assert.AreEqual(Sec, matched2.Fills[1].Security);
            Assert.AreEqual(Now3, matched2.Fills[1].Time);
            Assert.AreEqual(ClientId3, matched2.Fills[1].ClientId);
            Assert.AreEqual(OrderId3, matched2.Fills[1].OrderId);
            Assert.AreEqual(90, matched2.Fills[1].Price);
            Assert.AreEqual(3, matched2.Fills[1].Quantity);
            Assert.AreEqual(false, matched2.Fills[1].IsResting);
            Assert.AreEqual(ClientId3, matched2.Fills[1].Order.ClientId);
            Assert.AreEqual(OrderId3, matched2.Fills[1].Order.OrderId);
            Assert.AreEqual(Sec, matched2.Fills[1].Order.Security);
            Assert.AreEqual(Now3, matched2.Fills[1].Order.CreatedTime);
            Assert.AreEqual(Now3, matched2.Fills[1].Order.ModifiedTime);
            Assert.AreEqual(Now3, matched2.Fills[1].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched2.Fills[1].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched2.Fills[1].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched2.Fills[1].Order.OrderValidity);
            Assert.AreEqual(Side.Buy, matched2.Fills[1].Order.Side);
            Assert.AreEqual(100, matched2.Fills[1].Order.Price);
            Assert.IsNull(matched2.Fills[1].Order.StopPrice);
            Assert.AreEqual(8, matched2.Fills[1].Order.Quantity);
            Assert.AreEqual(8, matched2.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(0, matched2.Fills[1].Order.RemainingQuantity);
        }

        [Test]
        public void CreateLimitOrder_MatchAgainstOrderAtSamePriceByTimeAfterIncreaseQuantity()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 110, 5);
            TimeProvider.SetCurrentTime(Now2);
            Book.CreateLimitOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Buy, 110, 5);
            TimeProvider.SetCurrentTime(Now3);
            Book.UpdateLimitOrder(ClientId1, OrderId1, 110, 7);
            TimeProvider.SetCurrentTime(Now4);

            // act
            var events = Book.CreateLimitOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Sell, 100, 3);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(2, events.Count);

            var matched = events[1] as OrdersMatched;
            Assert.IsNotNull(matched);
            Assert.AreEqual(Sec, matched.Security);
            Assert.AreEqual(Now4, matched.Time);
            Assert.AreEqual(110, matched.Price);
            Assert.AreEqual(3, matched.Quantity);
            Assert.IsNotNull(matched.Fills);
            Assert.AreEqual(2, matched.Fills.Count);

            Assert.AreEqual(Sec, matched.Fills[0].Security);
            Assert.AreEqual(Now4, matched.Fills[0].Time);
            Assert.AreEqual(ClientId2, matched.Fills[0].ClientId);
            Assert.AreEqual(OrderId2, matched.Fills[0].OrderId);
            Assert.AreEqual(110, matched.Fills[0].Price);
            Assert.AreEqual(3, matched.Fills[0].Quantity);
            Assert.AreEqual(true, matched.Fills[0].IsResting);
            Assert.AreEqual(ClientId2, matched.Fills[0].Order.ClientId);
            Assert.AreEqual(OrderId2, matched.Fills[0].Order.OrderId);
            Assert.AreEqual(Sec, matched.Fills[0].Order.Security);
            Assert.AreEqual(Now2, matched.Fills[0].Order.CreatedTime);
            Assert.AreEqual(Now2, matched.Fills[0].Order.ModifiedTime);
            Assert.IsNull(matched.Fills[0].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched.Fills[0].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched.Fills[0].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched.Fills[0].Order.OrderValidity);
            Assert.AreEqual(Side.Buy, matched.Fills[0].Order.Side);
            Assert.AreEqual(110, matched.Fills[0].Order.Price);
            Assert.IsNull(matched.Fills[0].Order.StopPrice);
            Assert.AreEqual(5, matched.Fills[0].Order.Quantity);
            Assert.AreEqual(3, matched.Fills[0].Order.FilledQuantity);
            Assert.AreEqual(2, matched.Fills[0].Order.RemainingQuantity);

            Assert.AreEqual(Sec, matched.Fills[1].Security);
            Assert.AreEqual(Now4, matched.Fills[1].Time);
            Assert.AreEqual(ClientId3, matched.Fills[1].ClientId);
            Assert.AreEqual(OrderId3, matched.Fills[1].OrderId);
            Assert.AreEqual(110, matched.Fills[1].Price);
            Assert.AreEqual(3, matched.Fills[1].Quantity);
            Assert.AreEqual(false, matched.Fills[1].IsResting);
            Assert.AreEqual(ClientId3, matched.Fills[1].Order.ClientId);
            Assert.AreEqual(OrderId3, matched.Fills[1].Order.OrderId);
            Assert.AreEqual(Sec, matched.Fills[1].Order.Security);
            Assert.AreEqual(Now4, matched.Fills[1].Order.CreatedTime);
            Assert.AreEqual(Now4, matched.Fills[1].Order.ModifiedTime);
            Assert.AreEqual(Now4, matched.Fills[1].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched.Fills[1].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched.Fills[1].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched.Fills[1].Order.OrderValidity);
            Assert.AreEqual(Side.Sell, matched.Fills[1].Order.Side);
            Assert.AreEqual(100, matched.Fills[1].Order.Price);
            Assert.IsNull(matched.Fills[1].Order.StopPrice);
            Assert.AreEqual(3, matched.Fills[1].Order.Quantity);
            Assert.AreEqual(3, matched.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(0, matched.Fills[1].Order.RemainingQuantity);
        }

        [Test]
        public void CreateLimitOrder_MatchAgainstOrdersAtSamePriceByTimeAfterIncreaseQuantity()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 110, 5);
            TimeProvider.SetCurrentTime(Now2);
            Book.CreateLimitOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Buy, 110, 5);
            TimeProvider.SetCurrentTime(Now3);
            Book.UpdateLimitOrder(ClientId1, OrderId1, 110, 6);
            TimeProvider.SetCurrentTime(Now4);

            // act
            var events = Book.CreateLimitOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Sell, 100, 8);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(3, events.Count);

            var matched1 = events[1] as OrdersMatched;
            Assert.IsNotNull(matched1);
            Assert.AreEqual(Sec, matched1.Security);
            Assert.AreEqual(Now4, matched1.Time);
            Assert.AreEqual(110, matched1.Price);
            Assert.AreEqual(5, matched1.Quantity);
            Assert.IsNotNull(matched1.Fills);
            Assert.AreEqual(2, matched1.Fills.Count);

            Assert.AreEqual(Sec, matched1.Fills[0].Security);
            Assert.AreEqual(Now4, matched1.Fills[0].Time);
            Assert.AreEqual(ClientId2, matched1.Fills[0].ClientId);
            Assert.AreEqual(OrderId2, matched1.Fills[0].OrderId);
            Assert.AreEqual(110, matched1.Fills[0].Price);
            Assert.AreEqual(5, matched1.Fills[0].Quantity);
            Assert.AreEqual(true, matched1.Fills[0].IsResting);
            Assert.AreEqual(ClientId2, matched1.Fills[0].Order.ClientId);
            Assert.AreEqual(OrderId2, matched1.Fills[0].Order.OrderId);
            Assert.AreEqual(Sec, matched1.Fills[0].Order.Security);
            Assert.AreEqual(Now2, matched1.Fills[0].Order.CreatedTime);
            Assert.AreEqual(Now2, matched1.Fills[0].Order.ModifiedTime);
            Assert.AreEqual(Now4, matched1.Fills[0].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched1.Fills[0].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched1.Fills[0].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched1.Fills[0].Order.OrderValidity);
            Assert.AreEqual(Side.Buy, matched1.Fills[0].Order.Side);
            Assert.AreEqual(110, matched1.Fills[0].Order.Price);
            Assert.IsNull(matched1.Fills[0].Order.StopPrice);
            Assert.AreEqual(5, matched1.Fills[0].Order.Quantity);
            Assert.AreEqual(5, matched1.Fills[0].Order.FilledQuantity);
            Assert.AreEqual(0, matched1.Fills[0].Order.RemainingQuantity);

            Assert.AreEqual(Sec, matched1.Fills[1].Security);
            Assert.AreEqual(Now4, matched1.Fills[1].Time);
            Assert.AreEqual(ClientId3, matched1.Fills[1].ClientId);
            Assert.AreEqual(OrderId3, matched1.Fills[1].OrderId);
            Assert.AreEqual(110, matched1.Fills[1].Price);
            Assert.AreEqual(5, matched1.Fills[1].Quantity);
            Assert.AreEqual(false, matched1.Fills[1].IsResting);
            Assert.AreEqual(ClientId3, matched1.Fills[1].Order.ClientId);
            Assert.AreEqual(OrderId3, matched1.Fills[1].Order.OrderId);
            Assert.AreEqual(Sec, matched1.Fills[1].Order.Security);
            Assert.AreEqual(Now4, matched1.Fills[1].Order.CreatedTime);
            Assert.AreEqual(Now4, matched1.Fills[1].Order.ModifiedTime);
            Assert.IsNull(matched1.Fills[1].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched1.Fills[1].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched1.Fills[1].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched1.Fills[1].Order.OrderValidity);
            Assert.AreEqual(Side.Sell, matched1.Fills[1].Order.Side);
            Assert.AreEqual(100, matched1.Fills[1].Order.Price);
            Assert.IsNull(matched1.Fills[1].Order.StopPrice);
            Assert.AreEqual(8, matched1.Fills[1].Order.Quantity);
            Assert.AreEqual(5, matched1.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(3, matched1.Fills[1].Order.RemainingQuantity);

            var matched2 = events[2] as OrdersMatched;
            Assert.IsNotNull(matched2);
            Assert.AreEqual(Sec, matched2.Security);
            Assert.AreEqual(Now4, matched2.Time);
            Assert.AreEqual(110, matched2.Price);
            Assert.AreEqual(3, matched2.Quantity);
            Assert.IsNotNull(matched2.Fills);
            Assert.AreEqual(2, matched2.Fills.Count);

            Assert.AreEqual(Sec, matched2.Fills[0].Security);
            Assert.AreEqual(Now4, matched2.Fills[0].Time);
            Assert.AreEqual(ClientId1, matched2.Fills[0].ClientId);
            Assert.AreEqual(OrderId1, matched2.Fills[0].OrderId);
            Assert.AreEqual(110, matched2.Fills[0].Price);
            Assert.AreEqual(3, matched2.Fills[0].Quantity);
            Assert.AreEqual(true, matched2.Fills[0].IsResting);
            Assert.AreEqual(ClientId1, matched2.Fills[0].Order.ClientId);
            Assert.AreEqual(OrderId1, matched2.Fills[0].Order.OrderId);
            Assert.AreEqual(Sec, matched2.Fills[0].Order.Security);
            Assert.AreEqual(Now1, matched2.Fills[0].Order.CreatedTime);
            Assert.AreEqual(Now3, matched2.Fills[0].Order.ModifiedTime);
            Assert.IsNull(matched2.Fills[0].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched2.Fills[0].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched2.Fills[0].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched2.Fills[0].Order.OrderValidity);
            Assert.AreEqual(Side.Buy, matched2.Fills[0].Order.Side);
            Assert.AreEqual(110, matched2.Fills[0].Order.Price);
            Assert.IsNull(matched2.Fills[0].Order.StopPrice);
            Assert.AreEqual(6, matched2.Fills[0].Order.Quantity);
            Assert.AreEqual(3, matched2.Fills[0].Order.FilledQuantity);
            Assert.AreEqual(3, matched2.Fills[0].Order.RemainingQuantity);

            Assert.AreEqual(Sec, matched2.Fills[1].Security);
            Assert.AreEqual(Now4, matched2.Fills[1].Time);
            Assert.AreEqual(ClientId3, matched2.Fills[1].ClientId);
            Assert.AreEqual(OrderId3, matched2.Fills[1].OrderId);
            Assert.AreEqual(110, matched2.Fills[1].Price);
            Assert.AreEqual(3, matched2.Fills[1].Quantity);
            Assert.AreEqual(false, matched2.Fills[1].IsResting);
            Assert.AreEqual(ClientId3, matched2.Fills[1].Order.ClientId);
            Assert.AreEqual(OrderId3, matched2.Fills[1].Order.OrderId);
            Assert.AreEqual(Sec, matched2.Fills[1].Order.Security);
            Assert.AreEqual(Now4, matched2.Fills[1].Order.CreatedTime);
            Assert.AreEqual(Now4, matched2.Fills[1].Order.ModifiedTime);
            Assert.AreEqual(Now4, matched2.Fills[1].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched2.Fills[1].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched2.Fills[1].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched2.Fills[1].Order.OrderValidity);
            Assert.AreEqual(Side.Sell, matched2.Fills[1].Order.Side);
            Assert.AreEqual(100, matched2.Fills[1].Order.Price);
            Assert.IsNull(matched2.Fills[1].Order.StopPrice);
            Assert.AreEqual(8, matched2.Fills[1].Order.Quantity);
            Assert.AreEqual(8, matched2.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(0, matched2.Fills[1].Order.RemainingQuantity);
        }

        [Test]
        public void CreateLimitOrder_MatchAgainstOrderAtSamePriceByTimeAfterDecreaseQuantity()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 110, 5);
            TimeProvider.SetCurrentTime(Now2);
            Book.CreateLimitOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Buy, 110, 5);
            TimeProvider.SetCurrentTime(Now3);
            Book.UpdateLimitOrder(ClientId1, OrderId1, 110, 4);
            TimeProvider.SetCurrentTime(Now4);

            // act
            var events = Book.CreateLimitOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Sell, 100, 3);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(2, events.Count);

            var matched = events[1] as OrdersMatched;
            Assert.IsNotNull(matched);
            Assert.AreEqual(Sec, matched.Security);
            Assert.AreEqual(Now4, matched.Time);
            Assert.AreEqual(110, matched.Price);
            Assert.AreEqual(3, matched.Quantity);
            Assert.IsNotNull(matched.Fills);
            Assert.AreEqual(2, matched.Fills.Count);

            Assert.AreEqual(Sec, matched.Fills[0].Security);
            Assert.AreEqual(Now4, matched.Fills[0].Time);
            Assert.AreEqual(ClientId1, matched.Fills[0].ClientId);
            Assert.AreEqual(OrderId1, matched.Fills[0].OrderId);
            Assert.AreEqual(110, matched.Fills[0].Price);
            Assert.AreEqual(3, matched.Fills[0].Quantity);
            Assert.AreEqual(true, matched.Fills[0].IsResting);
            Assert.AreEqual(Sec, matched.Fills[0].Security);
            Assert.AreEqual(Now4, matched.Fills[0].Time);
            Assert.AreEqual(ClientId1, matched.Fills[0].ClientId);
            Assert.AreEqual(OrderId1, matched.Fills[0].OrderId);
            Assert.AreEqual(true, matched.Fills[0].IsResting);
            Assert.AreEqual(ClientId1, matched.Fills[0].Order.ClientId);
            Assert.AreEqual(OrderId1, matched.Fills[0].Order.OrderId);
            Assert.AreEqual(Sec, matched.Fills[0].Order.Security);
            Assert.AreEqual(Now1, matched.Fills[0].Order.CreatedTime);
            Assert.AreEqual(Now3, matched.Fills[0].Order.ModifiedTime);
            Assert.IsNull(matched.Fills[0].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched.Fills[0].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched.Fills[0].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched.Fills[0].Order.OrderValidity);
            Assert.AreEqual(Side.Buy, matched.Fills[0].Order.Side);
            Assert.AreEqual(110, matched.Fills[0].Order.Price);
            Assert.IsNull(matched.Fills[0].Order.StopPrice);
            Assert.AreEqual(4, matched.Fills[0].Order.Quantity);
            Assert.AreEqual(3, matched.Fills[0].Order.FilledQuantity);
            Assert.AreEqual(1, matched.Fills[0].Order.RemainingQuantity);

            Assert.AreEqual(Sec, matched.Fills[1].Security);
            Assert.AreEqual(Now4, matched.Fills[1].Time);
            Assert.AreEqual(ClientId3, matched.Fills[1].ClientId);
            Assert.AreEqual(OrderId3, matched.Fills[1].OrderId);
            Assert.AreEqual(110, matched.Fills[1].Price);
            Assert.AreEqual(3, matched.Fills[1].Quantity);
            Assert.AreEqual(false, matched.Fills[1].IsResting);
            Assert.AreEqual(ClientId3, matched.Fills[1].Order.ClientId);
            Assert.AreEqual(OrderId3, matched.Fills[1].Order.OrderId);
            Assert.AreEqual(Sec, matched.Fills[1].Order.Security);
            Assert.AreEqual(Now4, matched.Fills[1].Order.CreatedTime);
            Assert.AreEqual(Now4, matched.Fills[1].Order.ModifiedTime);
            Assert.AreEqual(Now4, matched.Fills[1].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched.Fills[1].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched.Fills[1].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched.Fills[1].Order.OrderValidity);
            Assert.AreEqual(Side.Sell, matched.Fills[1].Order.Side);
            Assert.AreEqual(100, matched.Fills[1].Order.Price);
            Assert.IsNull(matched.Fills[1].Order.StopPrice);
            Assert.AreEqual(3, matched.Fills[1].Order.Quantity);
            Assert.AreEqual(3, matched.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(0, matched.Fills[1].Order.RemainingQuantity);
        }

        [Test]
        public void CreateLimitOrder_MatchAgainstOrdersAtSamePriceByTimeAfterDecreaseQuantity()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 110, 5);
            TimeProvider.SetCurrentTime(Now2);
            Book.CreateLimitOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Buy, 110, 5);
            TimeProvider.SetCurrentTime(Now3);
            Book.UpdateLimitOrder(ClientId1, OrderId1, 110, 4);
            TimeProvider.SetCurrentTime(Now4);

            // act
            var events = Book.CreateLimitOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Sell, 100, 8);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(3, events.Count);

            var matched1 = events[1] as OrdersMatched;
            Assert.IsNotNull(matched1);
            Assert.AreEqual(Sec, matched1.Security);
            Assert.AreEqual(Now4, matched1.Time);
            Assert.AreEqual(110, matched1.Price);
            Assert.AreEqual(4, matched1.Quantity);
            Assert.IsNotNull(matched1.Fills);
            Assert.AreEqual(2, matched1.Fills.Count);

            Assert.AreEqual(Sec, matched1.Fills[0].Security);
            Assert.AreEqual(Now4, matched1.Fills[0].Time);
            Assert.AreEqual(ClientId1, matched1.Fills[0].ClientId);
            Assert.AreEqual(OrderId1, matched1.Fills[0].OrderId);
            Assert.AreEqual(110, matched1.Fills[0].Price);
            Assert.AreEqual(4, matched1.Fills[0].Quantity);
            Assert.AreEqual(true, matched1.Fills[0].IsResting);
            Assert.AreEqual(ClientId1, matched1.Fills[0].Order.ClientId);
            Assert.AreEqual(OrderId1, matched1.Fills[0].Order.OrderId);
            Assert.AreEqual(Sec, matched1.Fills[0].Order.Security);
            Assert.AreEqual(Now1, matched1.Fills[0].Order.CreatedTime);
            Assert.AreEqual(Now3, matched1.Fills[0].Order.ModifiedTime);
            Assert.AreEqual(Now4, matched1.Fills[0].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched1.Fills[0].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched1.Fills[0].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched1.Fills[0].Order.OrderValidity);
            Assert.AreEqual(Side.Buy, matched1.Fills[0].Order.Side);
            Assert.AreEqual(110, matched1.Fills[0].Order.Price);
            Assert.IsNull(matched1.Fills[0].Order.StopPrice);
            Assert.AreEqual(4, matched1.Fills[0].Order.Quantity);
            Assert.AreEqual(4, matched1.Fills[0].Order.FilledQuantity);
            Assert.AreEqual(0, matched1.Fills[0].Order.RemainingQuantity);

            Assert.AreEqual(Sec, matched1.Fills[1].Security);
            Assert.AreEqual(Now4, matched1.Fills[1].Time);
            Assert.AreEqual(ClientId3, matched1.Fills[1].ClientId);
            Assert.AreEqual(OrderId3, matched1.Fills[1].OrderId);
            Assert.AreEqual(110, matched1.Fills[1].Price);
            Assert.AreEqual(4, matched1.Fills[1].Quantity);
            Assert.AreEqual(false, matched1.Fills[1].IsResting);
            Assert.AreEqual(ClientId3, matched1.Fills[1].Order.ClientId);
            Assert.AreEqual(OrderId3, matched1.Fills[1].Order.OrderId);
            Assert.AreEqual(Sec, matched1.Fills[1].Order.Security);
            Assert.AreEqual(Now4, matched1.Fills[1].Order.CreatedTime);
            Assert.AreEqual(Now4, matched1.Fills[1].Order.ModifiedTime);
            Assert.IsNull(matched1.Fills[1].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched1.Fills[1].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched1.Fills[1].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched1.Fills[1].Order.OrderValidity);
            Assert.AreEqual(Side.Sell, matched1.Fills[1].Order.Side);
            Assert.AreEqual(100, matched1.Fills[1].Order.Price);
            Assert.IsNull(matched1.Fills[1].Order.StopPrice);
            Assert.AreEqual(8, matched1.Fills[1].Order.Quantity);
            Assert.AreEqual(4, matched1.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(4, matched1.Fills[1].Order.RemainingQuantity);

            var matched2 = events[2] as OrdersMatched;
            Assert.IsNotNull(matched2);
            Assert.AreEqual(Sec, matched2.Security);
            Assert.AreEqual(Now4, matched2.Time);
            Assert.AreEqual(110, matched2.Price);
            Assert.AreEqual(4, matched2.Quantity);
            Assert.IsNotNull(matched2.Fills);
            Assert.AreEqual(2, matched2.Fills.Count);

            Assert.AreEqual(Sec, matched2.Fills[0].Security);
            Assert.AreEqual(Now4, matched2.Fills[0].Time);
            Assert.AreEqual(ClientId2, matched2.Fills[0].ClientId);
            Assert.AreEqual(OrderId2, matched2.Fills[0].OrderId);
            Assert.AreEqual(110, matched2.Fills[0].Price);
            Assert.AreEqual(4, matched2.Fills[0].Quantity);
            Assert.AreEqual(true, matched2.Fills[0].IsResting);
            Assert.AreEqual(ClientId2, matched2.Fills[0].Order.ClientId);
            Assert.AreEqual(OrderId2, matched2.Fills[0].Order.OrderId);
            Assert.AreEqual(Sec, matched2.Fills[0].Order.Security);
            Assert.AreEqual(Now2, matched2.Fills[0].Order.CreatedTime);
            Assert.AreEqual(Now2, matched2.Fills[0].Order.ModifiedTime);
            Assert.IsNull(matched2.Fills[0].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched2.Fills[0].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched2.Fills[0].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched2.Fills[0].Order.OrderValidity);
            Assert.AreEqual(Side.Buy, matched2.Fills[0].Order.Side);
            Assert.AreEqual(110, matched2.Fills[0].Order.Price);
            Assert.IsNull(matched2.Fills[0].Order.StopPrice);
            Assert.AreEqual(5, matched2.Fills[0].Order.Quantity);
            Assert.AreEqual(4, matched2.Fills[0].Order.FilledQuantity);
            Assert.AreEqual(1, matched2.Fills[0].Order.RemainingQuantity);

            Assert.AreEqual(Sec, matched2.Fills[1].Security);
            Assert.AreEqual(Now4, matched2.Fills[1].Time);
            Assert.AreEqual(ClientId3, matched2.Fills[1].ClientId);
            Assert.AreEqual(OrderId3, matched2.Fills[1].OrderId);
            Assert.AreEqual(110, matched2.Fills[1].Price);
            Assert.AreEqual(4, matched2.Fills[1].Quantity);
            Assert.AreEqual(false, matched2.Fills[1].IsResting);
            Assert.AreEqual(ClientId3, matched2.Fills[1].Order.ClientId);
            Assert.AreEqual(OrderId3, matched2.Fills[1].Order.OrderId);
            Assert.AreEqual(Sec, matched2.Fills[1].Order.Security);
            Assert.AreEqual(Now4, matched2.Fills[1].Order.CreatedTime);
            Assert.AreEqual(Now4, matched2.Fills[1].Order.ModifiedTime);
            Assert.AreEqual(Now4, matched2.Fills[1].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched2.Fills[1].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched2.Fills[1].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched2.Fills[1].Order.OrderValidity);
            Assert.AreEqual(Side.Sell, matched2.Fills[1].Order.Side);
            Assert.AreEqual(100, matched2.Fills[1].Order.Price);
            Assert.IsNull(matched2.Fills[1].Order.StopPrice);
            Assert.AreEqual(8, matched2.Fills[1].Order.Quantity);
            Assert.AreEqual(8, matched2.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(0, matched2.Fills[1].Order.RemainingQuantity);
        }

        [Test]
        public void CreateLimitOrder_MarketClosed_Rejected()
        {
            // arrange
            // act
            var events = Book.CreateLimitOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 100, 3);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as CreateOrderRejected;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(Sec, rejected.Security);
            Assert.AreEqual(Now1, rejected.Time);
            Assert.AreEqual(OrderRejectedReason.MarketClosed, rejected.Reason);
            Assert.AreEqual(ClientId1, rejected.ClientId);
            Assert.AreEqual(OrderId1, rejected.OrderId);
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void CreateLimitOrder_InvalidQuantity_Rejected(int quantity)
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);

            // act
            var events = Book.CreateLimitOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 100, quantity);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as CreateOrderRejected;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(Sec, rejected.Security);
            Assert.AreEqual(Now1, rejected.Time);
            Assert.AreEqual(OrderRejectedReason.InvalidQuantity, rejected.Reason);
            Assert.AreEqual(ClientId1, rejected.ClientId);
            Assert.AreEqual(OrderId1, rejected.OrderId);
        }

        [TestCase(8)]
        [TestCase(-8)]
        [TestCase(-108)]
        [TestCase(10.01)]
        public void CreateLimitOrder_InvalidPriceIncrement_Rejected(decimal price)
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);

            // act
            var events = Book.CreateLimitOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, price, 6);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as CreateOrderRejected;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(Sec, rejected.Security);
            Assert.AreEqual(Now1, rejected.Time);
            Assert.AreEqual(OrderRejectedReason.InvalidPriceIncrement, rejected.Reason);
            Assert.AreEqual(ClientId1, rejected.ClientId);
            Assert.AreEqual(OrderId1, rejected.OrderId);
        }

        [Test]
        public void UpdateLimitOrder_IncreaseQuantity_Success()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 100, 3);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.UpdateLimitOrder(ClientId1, OrderId1, 110, 5);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var updated = events[0] as UpdateOrderConfirmed;
            Assert.IsNotNull(updated);
            Assert.AreEqual(Sec, updated.Security);
            Assert.AreEqual(Now2, updated.Time);
            Assert.AreEqual(ClientId1, updated.ClientId);
            Assert.AreEqual(ClientId1, updated.Order.ClientId);
            Assert.AreEqual(OrderId1, updated.Order.OrderId);
            Assert.AreEqual(Sec, updated.Order.Security);
            Assert.AreEqual(Now1, updated.Order.CreatedTime);
            Assert.AreEqual(Now2, updated.Order.ModifiedTime);
            Assert.IsNull(updated.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, updated.Order.Status);
            Assert.AreEqual(OrderType.Limit, updated.Order.Type);
            Assert.AreEqual(OrderValidity.Day, updated.Order.OrderValidity);
            Assert.AreEqual(Side.Buy, updated.Order.Side);
            Assert.AreEqual(110, updated.Order.Price);
            Assert.IsNull(updated.Order.StopPrice);
            Assert.AreEqual(5, updated.Order.Quantity);
            Assert.AreEqual(0, updated.Order.FilledQuantity);
            Assert.AreEqual(5, updated.Order.RemainingQuantity);
        }

        [Test]
        public void UpdateLimitOrder_DecreaseQuantity_Success()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 100, 3);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.UpdateLimitOrder(ClientId1, OrderId1, 110, 1);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var updated = events[0] as UpdateOrderConfirmed;
            Assert.IsNotNull(updated);
            Assert.AreEqual(Sec, updated.Security);
            Assert.AreEqual(Now2, updated.Time);
            Assert.AreEqual(ClientId1, updated.ClientId);
            Assert.AreEqual(ClientId1, updated.Order.ClientId);
            Assert.AreEqual(OrderId1, updated.Order.OrderId);
            Assert.AreEqual(Sec, updated.Order.Security);
            Assert.AreEqual(Now1, updated.Order.CreatedTime);
            Assert.AreEqual(Now2, updated.Order.ModifiedTime);
            Assert.IsNull(updated.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, updated.Order.Status);
            Assert.AreEqual(OrderType.Limit, updated.Order.Type);
            Assert.AreEqual(OrderValidity.Day, updated.Order.OrderValidity);
            Assert.AreEqual(Side.Buy, updated.Order.Side);
            Assert.AreEqual(110, updated.Order.Price);
            Assert.IsNull(updated.Order.StopPrice);
            Assert.AreEqual(1, updated.Order.Quantity);
            Assert.AreEqual(0, updated.Order.FilledQuantity);
            Assert.AreEqual(1, updated.Order.RemainingQuantity);
        }

        [Test]
        public void UpdateLimitOrder_DecreaseQuantityBelowFilled_Cancelled()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 100, 5);
            Book.CreateLimitOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 100, 4);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.UpdateLimitOrder(ClientId1, OrderId1, 100, 2);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var cancelled = events[0] as CancelOrderConfirmed;
            Assert.IsNotNull(cancelled);
            Assert.AreEqual(Sec, cancelled.Security);
            Assert.AreEqual(Now2, cancelled.Time);
            Assert.AreEqual(ClientId1, cancelled.ClientId);
            Assert.AreEqual(OrderCancelledReason.UpdatedQuantityLowerThanFilledQuantity, cancelled.Reason);
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
            Assert.IsNull(cancelled.Order.StopPrice);
            Assert.AreEqual(5, cancelled.Order.Quantity);
            Assert.AreEqual(4, cancelled.Order.FilledQuantity);
            Assert.AreEqual(0, cancelled.Order.RemainingQuantity);
        }

        [Test]
        public void UpdateLimitOrder_MatchAgainstOrderByTime()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Sell, 90, 5);
            TimeProvider.SetCurrentTime(Now2);
            Book.CreateLimitOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Buy, 80, 5);
            TimeProvider.SetCurrentTime(Now3);

            // act
            var events = Book.UpdateLimitOrder(ClientId1, OrderId1, 70, 3);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(2, events.Count);

            var matched = events[1] as OrdersMatched;
            Assert.IsNotNull(matched);
            Assert.AreEqual(Sec, matched.Security);
            Assert.AreEqual(Now3, matched.Time);
            Assert.AreEqual(80, matched.Price);
            Assert.AreEqual(3, matched.Quantity);
            Assert.IsNotNull(matched.Fills);
            Assert.AreEqual(2, matched.Fills.Count);

            Assert.AreEqual(Sec, matched.Fills[0].Security);
            Assert.AreEqual(Now3, matched.Fills[0].Time);
            Assert.AreEqual(ClientId2, matched.Fills[0].ClientId);
            Assert.AreEqual(OrderId2, matched.Fills[0].OrderId);
            Assert.AreEqual(80, matched.Fills[0].Price);
            Assert.AreEqual(3, matched.Fills[0].Quantity);
            Assert.AreEqual(true, matched.Fills[0].IsResting);
            Assert.AreEqual(ClientId2, matched.Fills[0].Order.ClientId);
            Assert.AreEqual(OrderId2, matched.Fills[0].Order.OrderId);
            Assert.AreEqual(Sec, matched.Fills[0].Order.Security);
            Assert.AreEqual(Now2, matched.Fills[0].Order.CreatedTime);
            Assert.AreEqual(Now2, matched.Fills[0].Order.ModifiedTime);
            Assert.IsNull(matched.Fills[0].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched.Fills[0].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched.Fills[0].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched.Fills[0].Order.OrderValidity);
            Assert.AreEqual(Side.Buy, matched.Fills[0].Order.Side);
            Assert.AreEqual(80, matched.Fills[0].Order.Price);
            Assert.IsNull(matched.Fills[0].Order.StopPrice);
            Assert.AreEqual(5, matched.Fills[0].Order.Quantity);
            Assert.AreEqual(3, matched.Fills[0].Order.FilledQuantity);
            Assert.AreEqual(2, matched.Fills[0].Order.RemainingQuantity);

            Assert.AreEqual(Sec, matched.Fills[1].Security);
            Assert.AreEqual(Now3, matched.Fills[1].Time);
            Assert.AreEqual(ClientId1, matched.Fills[1].ClientId);
            Assert.AreEqual(OrderId1, matched.Fills[1].OrderId);
            Assert.AreEqual(80, matched.Fills[1].Price);
            Assert.AreEqual(3, matched.Fills[1].Quantity);
            Assert.AreEqual(false, matched.Fills[1].IsResting);
            Assert.AreEqual(ClientId1, matched.Fills[1].Order.ClientId);
            Assert.AreEqual(OrderId1, matched.Fills[1].Order.OrderId);
            Assert.AreEqual(Sec, matched.Fills[1].Order.Security);
            Assert.AreEqual(Now1, matched.Fills[1].Order.CreatedTime);
            Assert.AreEqual(Now3, matched.Fills[1].Order.ModifiedTime);
            Assert.AreEqual(Now3, matched.Fills[1].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched.Fills[1].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched.Fills[1].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched.Fills[1].Order.OrderValidity);
            Assert.AreEqual(Side.Sell, matched.Fills[1].Order.Side);
            Assert.AreEqual(70, matched.Fills[1].Order.Price);
            Assert.IsNull(matched.Fills[1].Order.StopPrice);
            Assert.AreEqual(3, matched.Fills[1].Order.Quantity);
            Assert.AreEqual(3, matched.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(0, matched.Fills[1].Order.RemainingQuantity);
        }

        [Test]
        public void UpdateLimitOrder_OrderFilled_Rejected()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 100, 3);
            Book.CreateLimitOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 100, 3);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.UpdateLimitOrder(ClientId1, OrderId1, 110, 5);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as UpdateOrderRejected;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(Sec, rejected.Security);
            Assert.AreEqual(Now2, rejected.Time);
            Assert.AreEqual(ClientId1, rejected.ClientId);
            Assert.AreEqual(OrderId1, rejected.OrderId);
            Assert.AreEqual(OrderRejectedReason.TooLateToCancel, rejected.Reason);
        }

        [Test]
        public void UpdateLimitOrder_OrderCancelled_Rejected()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 100, 3);
            Book.CancelOrder(ClientId1, OrderId1);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.UpdateLimitOrder(ClientId1, OrderId1, 110, 5);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as UpdateOrderRejected;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(Sec, rejected.Security);
            Assert.AreEqual(Now2, rejected.Time);
            Assert.AreEqual(ClientId1, rejected.ClientId);
            Assert.AreEqual(OrderId1, rejected.OrderId);
            Assert.AreEqual(OrderRejectedReason.TooLateToCancel, rejected.Reason);
        }

        [Test]
        public void UpdateLimitOrder_OrderExpired_Rejected()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 100, 3);
            Book.UpdateStatus(OrderBookStatus.Closed);
            Book.UpdateStatus(OrderBookStatus.Open);

            // act
            var events = Book.UpdateLimitOrder(ClientId1, OrderId1, 110, 5);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as UpdateOrderRejected;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(Sec, rejected.Security);
            Assert.AreEqual(Now1, rejected.Time);
            Assert.AreEqual(ClientId1, rejected.ClientId);
            Assert.AreEqual(OrderId1, rejected.OrderId);
            Assert.AreEqual(OrderRejectedReason.TooLateToCancel, rejected.Reason);
        }

        [Test]
        public void UpdateLimitOrder_NotFound_Rejected()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);

            // act
            var events = Book.UpdateLimitOrder(ClientId1, OrderId1, 110, 5);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as UpdateOrderRejected;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(Sec, rejected.Security);
            Assert.AreEqual(Now1, rejected.Time);
            Assert.AreEqual(ClientId1, rejected.ClientId);
            Assert.AreEqual(OrderId1, rejected.OrderId);
            Assert.AreEqual(OrderRejectedReason.OrderNotInBook, rejected.Reason);
        }

        [Test]
        public void UpdateLimitOrder_MarketClosed_Rejected()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 100, 3);
            Book.UpdateStatus(OrderBookStatus.Closed);

            // act
            var events = Book.UpdateLimitOrder(ClientId1, OrderId1, 105, 5);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as UpdateOrderRejected;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(Sec, rejected.Security);
            Assert.AreEqual(Now1, rejected.Time);
            Assert.AreEqual(ClientId1, rejected.ClientId);
            Assert.AreEqual(OrderId1, rejected.OrderId);
            Assert.AreEqual(OrderRejectedReason.MarketClosed, rejected.Reason);
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void UpdateLimitOrder_InvalidQuantity_Rejected(int quantity)
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 100, 3);

            // act
            var events = Book.UpdateLimitOrder(ClientId1, OrderId1, 110, quantity);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as UpdateOrderRejected;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(Sec, rejected.Security);
            Assert.AreEqual(Now1, rejected.Time);
            Assert.AreEqual(ClientId1, rejected.ClientId);
            Assert.AreEqual(OrderId1, rejected.OrderId);
            Assert.AreEqual(OrderRejectedReason.InvalidQuantity, rejected.Reason);
        }

        [TestCase(8)]
        [TestCase(-8)]
        [TestCase(-108)]
        [TestCase(10.01)]
        public void UpdateLimitOrder_InvalidPriceIncrement_Rejected(decimal price)
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 100, 6);

            // act
            var events = Book.UpdateLimitOrder(ClientId1, OrderId1, price, 6);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as UpdateOrderRejected;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(Sec, rejected.Security);
            Assert.AreEqual(Now1, rejected.Time);
            Assert.AreEqual(ClientId1, rejected.ClientId);
            Assert.AreEqual(OrderId1, rejected.OrderId);
            Assert.AreEqual(OrderRejectedReason.InvalidPriceIncrement, rejected.Reason);
        }

        [Test]
        public void CancelLimitOrder_Valid_Success()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 100, 3);
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
            Assert.IsNull(cancelled.Order.StopPrice);
            Assert.AreEqual(3, cancelled.Order.Quantity);
            Assert.AreEqual(0, cancelled.Order.FilledQuantity);
            Assert.AreEqual(0, cancelled.Order.RemainingQuantity);
        }

        [Test]
        public void CancelLimitOrder_NotFound_Rejected()
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

        [Test]
        public void CancelLimitOrder_MarketClosed_Rejected()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 100, 3);
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
        public void CreateMarketOrder_Valid_Success()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10, 20);
            var book = new InMemoryOrderBook(sec, TimeProvider);
            book.UpdateStatus(OrderBookStatus.Open);
            book.CreateLimitOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 500, 3);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = book.CreateMarketOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 5);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(2, events.Count);

            var matched = events[1] as OrdersMatched;
            Assert.IsNotNull(matched);
            Assert.AreEqual(sec, matched.Security);
            Assert.AreEqual(Now2, matched.Time);
            Assert.AreEqual(500, matched.Price);
            Assert.AreEqual(3, matched.Quantity);
            Assert.IsNotNull(matched.Fills);
            Assert.AreEqual(2, matched.Fills.Count);

            Assert.AreEqual(sec, matched.Fills[0].Security);
            Assert.AreEqual(Now2, matched.Fills[0].Time);
            Assert.AreEqual(ClientId1, matched.Fills[0].ClientId);
            Assert.AreEqual(OrderId1, matched.Fills[0].OrderId);
            Assert.AreEqual(500, matched.Fills[0].Price);
            Assert.AreEqual(3, matched.Fills[0].Quantity);
            Assert.AreEqual(true, matched.Fills[0].IsResting);
            Assert.AreEqual(ClientId1, matched.Fills[0].Order.ClientId);
            Assert.AreEqual(OrderId1, matched.Fills[0].Order.OrderId);
            Assert.AreEqual(sec, matched.Fills[0].Order.Security);
            Assert.AreEqual(Now1, matched.Fills[0].Order.CreatedTime);
            Assert.AreEqual(Now1, matched.Fills[0].Order.ModifiedTime);
            Assert.AreEqual(Now2, matched.Fills[0].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched.Fills[0].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched.Fills[0].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched.Fills[0].Order.OrderValidity);
            Assert.AreEqual(Side.Buy, matched.Fills[0].Order.Side);
            Assert.AreEqual(500, matched.Fills[0].Order.Price);
            Assert.IsNull(matched.Fills[0].Order.StopPrice);
            Assert.AreEqual(3, matched.Fills[0].Order.Quantity);
            Assert.AreEqual(3, matched.Fills[0].Order.FilledQuantity);
            Assert.AreEqual(0, matched.Fills[0].Order.RemainingQuantity);

            Assert.AreEqual(sec, matched.Fills[1].Security);
            Assert.AreEqual(Now2, matched.Fills[1].Time);
            Assert.AreEqual(ClientId2, matched.Fills[1].ClientId);
            Assert.AreEqual(OrderId2, matched.Fills[1].OrderId);
            Assert.AreEqual(500, matched.Fills[1].Price);
            Assert.AreEqual(3, matched.Fills[1].Quantity);
            Assert.AreEqual(false, matched.Fills[1].IsResting);
            Assert.AreEqual(ClientId2, matched.Fills[1].Order.ClientId);
            Assert.AreEqual(OrderId2, matched.Fills[1].Order.OrderId);
            Assert.AreEqual(sec, matched.Fills[1].Order.Security);
            Assert.AreEqual(Now2, matched.Fills[1].Order.CreatedTime);
            Assert.AreEqual(Now2, matched.Fills[1].Order.ModifiedTime);
            Assert.IsNull(matched.Fills[1].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched.Fills[1].Order.Status);
            Assert.AreEqual(OrderType.Market, matched.Fills[1].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched.Fills[1].Order.OrderValidity);
            Assert.AreEqual(Side.Sell, matched.Fills[1].Order.Side);
            Assert.AreEqual(300, matched.Fills[1].Order.Price);
            Assert.IsNull(matched.Fills[1].Order.StopPrice);
            Assert.AreEqual(5, matched.Fills[1].Order.Quantity);
            Assert.AreEqual(3, matched.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(2, matched.Fills[1].Order.RemainingQuantity);
        }

        [Test]
        public void CreateMarketOrder_MarketClosed_Rejected()
        {
            // arrange
            // act
            var events = Book.CreateMarketOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as CreateOrderRejected;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(Sec, rejected.Security);
            Assert.AreEqual(Now1, rejected.Time);
            Assert.AreEqual(ClientId1, rejected.ClientId);
            Assert.AreEqual(OrderId1, rejected.OrderId);
            Assert.AreEqual(OrderRejectedReason.MarketClosed, rejected.Reason);
        }

        [Test]
        public void CreateMarketOrder_MarketPreOpen_Rejected()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.PreOpen);

            // act
            var events = Book.CreateMarketOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as CreateOrderRejected;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(Sec, rejected.Security);
            Assert.AreEqual(Now1, rejected.Time);
            Assert.AreEqual(ClientId1, rejected.ClientId);
            Assert.AreEqual(OrderId1, rejected.OrderId);
            Assert.AreEqual(OrderRejectedReason.MarketPreOpen, rejected.Reason);
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void CreateMarketOrder_InvalidQuantity_Rejected(int quantity)
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);

            // act
            var events = Book.CreateMarketOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, quantity);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as CreateOrderRejected;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(Sec, rejected.Security);
            Assert.AreEqual(Now1, rejected.Time);
            Assert.AreEqual(ClientId1, rejected.ClientId);
            Assert.AreEqual(OrderId1, rejected.OrderId);
            Assert.AreEqual(OrderRejectedReason.InvalidQuantity, rejected.Reason);
        }

        [Test]
        public void CreateMarketOrder_EmptyBook_Rejected()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);

            // act
            var events = Book.CreateMarketOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as CreateOrderRejected;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(Sec, rejected.Security);
            Assert.AreEqual(Now1, rejected.Time);
            Assert.AreEqual(ClientId1, rejected.ClientId);
            Assert.AreEqual(OrderId1, rejected.OrderId);
            Assert.AreEqual(OrderRejectedReason.NoOrdersToMatchMarketOrder, rejected.Reason);
        }

        [TestCase(OrderBookStatus.PreOpen)]
        [TestCase(OrderBookStatus.Open)]
        [TestCase(OrderBookStatus.Closed)]
        public void UpdateStatus_StatusChanged(OrderBookStatus status)
        {
            // arrange
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
        public void UpdateStatus_Open_MatchPreOpenOrders()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.PreOpen);
            Book.CreateLimitOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 100, 5);
            TimeProvider.SetCurrentTime(Now2);
            Book.CreateLimitOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 100, 5);
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
            Assert.IsNull(matched.Fills[0].Order.StopPrice);
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
            Assert.IsNull(matched.Fills[1].Order.StopPrice);
            Assert.AreEqual(5, matched.Fills[1].Order.Quantity);
            Assert.AreEqual(5, matched.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(0, matched.Fills[1].Order.RemainingQuantity);
        }

        [Test]
        public void UpdateStatus_Closed_ExpireDayOrders()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateLimitOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 100, 5);
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
            Assert.IsNull(expired.Order.StopPrice);
            Assert.AreEqual(5, expired.Order.Quantity);
            Assert.AreEqual(0, expired.Order.FilledQuantity);
            Assert.AreEqual(0, expired.Order.RemainingQuantity);
        }

        [Test]
        public void Process_CreateLimitOrder_Success()
        {
            // act
            Book.Process(new CreateLimitOrder(Sec, ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 100, 3));
        }

        [Test]
        public void Process_CreateMarketOrder_Success()
        {
            // act
            Book.Process(new CreateMarketOrder(Sec, ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3));
        }

        [Test]
        public void Process_UpdateMarketOrder_Success()
        {
            // act
            Book.Process(new UpdateLimitOrder(Sec, ClientId1, OrderId1, 100, 3));
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