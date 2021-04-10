using System;
using Circus.OrderBook;
using Circus.TimeProviders;
using NUnit.Framework;

namespace Circus.Tests.OrderBook
{
    [TestFixture]
    public class InMemoryOrderBookCreateOrderTests
    {
        private static readonly Security Sec = new("GCZ6", SecurityType.Future, 10, 10);

        private static readonly DateTime Now1 = new(2000, 1, 1, 12, 0, 0);
        private static readonly DateTime Now2 = new(2000, 1, 1, 12, 1, 0);
        private static readonly DateTime Now3 = new(2000, 1, 1, 12, 2, 0);
        private static readonly DateTime Now4 = new(2000, 1, 1, 12, 3, 0);

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

        [TestCase(Side.Buy)]
        [TestCase(Side.Sell)]
        public void LimitOrder_Success(Side side)
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);

            // act
            var events = Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, side, 3, 100);

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
            Assert.AreEqual(side, created.Order.Side);
            Assert.AreEqual(100, created.Order.Price);
            Assert.IsNull(created.Order.TriggerPrice);
            Assert.AreEqual(3, created.Order.Quantity);
            Assert.AreEqual(0, created.Order.FilledQuantity);
            Assert.AreEqual(3, created.Order.RemainingQuantity);
        }

        [TestCase(Side.Buy, 700)]
        [TestCase(Side.Sell, 300)]
        public void MarketOrder_Success(Side side, decimal limitPrice)
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10, 20);
            var book = new InMemoryOrderBook(sec, TimeProvider);
            book.UpdateStatus(OrderBookStatus.Open);
            book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, side == Side.Buy ? Side.Sell : Side.Buy, 3, 500);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, side, 5);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(2, events.Count);

            var created = events[0] as CreateOrderConfirmed;
            Assert.IsNotNull(created);
            Assert.AreEqual(sec, created.Security);
            Assert.AreEqual(Now2, created.Time);
            Assert.AreEqual(ClientId2, created.ClientId);
            Assert.AreEqual(ClientId2, created.Order.ClientId);
            Assert.AreEqual(OrderId2, created.Order.OrderId);
            Assert.AreEqual(sec, created.Order.Security);
            Assert.AreEqual(Now2, created.Order.CreatedTime);
            Assert.AreEqual(Now2, created.Order.ModifiedTime);
            Assert.IsNull(created.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, created.Order.Status);
            Assert.AreEqual(OrderType.Limit, created.Order.Type);
            Assert.AreEqual(OrderValidity.Day, created.Order.OrderValidity);
            Assert.AreEqual(side, created.Order.Side);
            Assert.AreEqual(limitPrice, created.Order.Price);
            Assert.IsNull(created.Order.TriggerPrice);
            Assert.AreEqual(5, created.Order.Quantity);
            Assert.AreEqual(0, created.Order.FilledQuantity);
            Assert.AreEqual(5, created.Order.RemainingQuantity);

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
            Assert.AreEqual(side == Side.Buy ? Side.Sell : Side.Buy, matched.Fills[0].Order.Side);
            Assert.AreEqual(500, matched.Fills[0].Order.Price);
            Assert.IsNull(matched.Fills[0].Order.TriggerPrice);
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
            Assert.AreEqual(OrderType.Limit, matched.Fills[1].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched.Fills[1].Order.OrderValidity);
            Assert.AreEqual(side, matched.Fills[1].Order.Side);
            Assert.AreEqual(limitPrice, matched.Fills[1].Order.Price);
            Assert.IsNull(matched.Fills[1].Order.TriggerPrice);
            Assert.AreEqual(5, matched.Fills[1].Order.Quantity);
            Assert.AreEqual(3, matched.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(2, matched.Fills[1].Order.RemainingQuantity);
        }

        [TestCase(Side.Buy, 520, 510)]
        [TestCase(Side.Sell, 490, 490)]
        public void StopLimitOrder_Success(Side side, decimal price, decimal triggerPrice)
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3, 500);
            Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 2, 500);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.CreateOrder(ClientId3, OrderId3, OrderValidity.Day, side, 5, price, triggerPrice);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);

            var created = events[0] as CreateOrderConfirmed;
            Assert.IsNotNull(created);
            Assert.AreEqual(Sec, created.Security);
            Assert.AreEqual(Now2, created.Time);
            Assert.AreEqual(ClientId3, created.ClientId);
            Assert.AreEqual(ClientId3, created.Order.ClientId);
            Assert.AreEqual(OrderId3, created.Order.OrderId);
            Assert.AreEqual(Sec, created.Order.Security);
            Assert.AreEqual(Now2, created.Order.CreatedTime);
            Assert.AreEqual(Now2, created.Order.ModifiedTime);
            Assert.IsNull(created.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Hidden, created.Order.Status);
            Assert.AreEqual(OrderType.StopLimit, created.Order.Type);
            Assert.AreEqual(OrderValidity.Day, created.Order.OrderValidity);
            Assert.AreEqual(side, created.Order.Side);
            Assert.AreEqual(price, created.Order.Price);
            Assert.AreEqual(triggerPrice, created.Order.TriggerPrice);
            Assert.AreEqual(5, created.Order.Quantity);
            Assert.AreEqual(0, created.Order.FilledQuantity);
            Assert.AreEqual(5, created.Order.RemainingQuantity);
        }
        
        [TestCase(Side.Buy, 510)]
        [TestCase(Side.Sell, 490)]
        public void StopMarketOrder_Success(Side side, decimal triggerPrice)
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3, 500);
            Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 2, 500);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.CreateOrder(ClientId3, OrderId3, OrderValidity.Day, side, 5, null, triggerPrice);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);

            var created = events[0] as CreateOrderConfirmed;
            Assert.IsNotNull(created);
            Assert.AreEqual(Sec, created.Security);
            Assert.AreEqual(Now2, created.Time);
            Assert.AreEqual(ClientId3, created.ClientId);
            Assert.AreEqual(ClientId3, created.Order.ClientId);
            Assert.AreEqual(OrderId3, created.Order.OrderId);
            Assert.AreEqual(Sec, created.Order.Security);
            Assert.AreEqual(Now2, created.Order.CreatedTime);
            Assert.AreEqual(Now2, created.Order.ModifiedTime);
            Assert.IsNull(created.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Hidden, created.Order.Status);
            Assert.AreEqual(OrderType.StopMarket, created.Order.Type);
            Assert.AreEqual(OrderValidity.Day, created.Order.OrderValidity);
            Assert.AreEqual(side, created.Order.Side);
            Assert.IsNull(created.Order.Price);
            Assert.AreEqual(triggerPrice, created.Order.TriggerPrice);
            Assert.AreEqual(5, created.Order.Quantity);
            Assert.AreEqual(0, created.Order.FilledQuantity);
            Assert.AreEqual(5, created.Order.RemainingQuantity);
        }

        [Test]
        public void LimitOrder_MatchAtSamePriceWithAggressorRemaining_Success()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3, 100);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 5, 100);

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
            Assert.IsNull(matched.Fills[0].Order.TriggerPrice);
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
            Assert.IsNull(matched.Fills[1].Order.TriggerPrice);
            Assert.AreEqual(5, matched.Fills[1].Order.Quantity);
            Assert.AreEqual(3, matched.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(2, matched.Fills[1].Order.RemainingQuantity);
        }

        [Test]
        public void LimitOrder_MatchAtDifferentPriceWithAggressorRemaining_Success()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3, 110);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 5, 100);

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
            Assert.IsNull(matched.Fills[0].Order.TriggerPrice);
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
            Assert.IsNull(matched.Fills[1].Order.TriggerPrice);
            Assert.AreEqual(5, matched.Fills[1].Order.Quantity);
            Assert.AreEqual(3, matched.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(2, matched.Fills[1].Order.RemainingQuantity);
        }

        [Test]
        public void LimitOrder_MatchAtDifferentPriceWithRestingRemaining_Success()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 5, 110);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 3, 100);

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
            Assert.IsNull(matched.Fills[0].Order.TriggerPrice);
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
            Assert.IsNull(matched.Fills[1].Order.TriggerPrice);
            Assert.AreEqual(3, matched.Fills[1].Order.Quantity);
            Assert.AreEqual(3, matched.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(0, matched.Fills[1].Order.RemainingQuantity);
        }

        [Test]
        public void LimitOrder_MatchSellAgainstOrderByTime()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 5, 110);
            TimeProvider.SetCurrentTime(Now2);
            Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Buy, 5, 120);
            TimeProvider.SetCurrentTime(Now3);

            // act
            var events = Book.CreateOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Sell, 3, 100);

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
            Assert.IsNull(matched.Fills[0].Order.TriggerPrice);
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
            Assert.IsNull(matched.Fills[1].Order.TriggerPrice);
            Assert.AreEqual(3, matched.Fills[1].Order.Quantity);
            Assert.AreEqual(3, matched.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(0, matched.Fills[1].Order.RemainingQuantity);
        }

        [Test]
        public void LimitOrder_MatchSellAgainstOrdersByPrice()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 5, 110);
            TimeProvider.SetCurrentTime(Now2);
            Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Buy, 5, 120);
            TimeProvider.SetCurrentTime(Now3);

            // act
            var events = Book.CreateOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Sell, 8, 100);

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
            Assert.IsNull(matched1.Fills[0].Order.TriggerPrice);
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
            Assert.IsNull(matched1.Fills[1].Order.TriggerPrice);
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
            Assert.IsNull(matched2.Fills[0].Order.TriggerPrice);
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
            Assert.IsNull(matched2.Fills[1].Order.TriggerPrice);
            Assert.AreEqual(8, matched2.Fills[1].Order.Quantity);
            Assert.AreEqual(8, matched2.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(0, matched2.Fills[1].Order.RemainingQuantity);
        }

        [Test]
        public void LimitOrder_MatchSellAgainstOrderAtSamePriceByTime()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 5, 110);
            TimeProvider.SetCurrentTime(Now2);
            Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Buy, 5, 110);
            TimeProvider.SetCurrentTime(Now3);

            // act
            var events = Book.CreateOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Sell, 3, 100);

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
            Assert.IsNull(matched.Fills[0].Order.TriggerPrice);
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
            Assert.IsNull(matched.Fills[1].Order.TriggerPrice);
            Assert.AreEqual(3, matched.Fills[1].Order.Quantity);
            Assert.AreEqual(3, matched.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(0, matched.Fills[1].Order.RemainingQuantity);
        }

        [Test]
        public void LimitOrder_MatchSellAgainstOrdersAtSamePriceByTime()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 5, 110);
            TimeProvider.SetCurrentTime(Now2);
            Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Buy, 5, 110);
            TimeProvider.SetCurrentTime(Now3);

            // act
            var events = Book.CreateOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Sell, 8, 100);

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
            Assert.IsNull(matched1.Fills[0].Order.TriggerPrice);
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
            Assert.IsNull(matched1.Fills[1].Order.TriggerPrice);
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
            Assert.IsNull(matched2.Fills[0].Order.TriggerPrice);
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
            Assert.IsNull(matched2.Fills[1].Order.TriggerPrice);
            Assert.AreEqual(8, matched2.Fills[1].Order.Quantity);
            Assert.AreEqual(8, matched2.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(0, matched2.Fills[1].Order.RemainingQuantity);
        }

        [Test]
        public void LimitOrder_MatchBuyAgainstOrderByTime()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Sell, 5, 90);
            TimeProvider.SetCurrentTime(Now2);
            Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 5, 80);
            TimeProvider.SetCurrentTime(Now3);

            // act
            var events = Book.CreateOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Buy, 3, 100);

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
            Assert.IsNull(matched.Fills[0].Order.TriggerPrice);
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
            Assert.IsNull(matched.Fills[1].Order.TriggerPrice);
            Assert.AreEqual(3, matched.Fills[1].Order.Quantity);
            Assert.AreEqual(3, matched.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(0, matched.Fills[1].Order.RemainingQuantity);
        }

        [Test]
        public void LimitOrder_MatchBuyAgainstOrdersByPrice()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Sell, 5, 90);
            TimeProvider.SetCurrentTime(Now2);
            Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 5, 80);
            TimeProvider.SetCurrentTime(Now3);

            // act
            var events = Book.CreateOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Buy, 8, 100);

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
            Assert.IsNull(matched1.Fills[0].Order.TriggerPrice);
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
            Assert.IsNull(matched1.Fills[1].Order.TriggerPrice);
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
            Assert.IsNull(matched2.Fills[0].Order.TriggerPrice);
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
            Assert.IsNull(matched2.Fills[1].Order.TriggerPrice);
            Assert.AreEqual(8, matched2.Fills[1].Order.Quantity);
            Assert.AreEqual(8, matched2.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(0, matched2.Fills[1].Order.RemainingQuantity);
        }

        [Test]
        public void LimitOrder_MatchBuyAgainstOrderAtSamePriceByTime()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Sell, 5, 90);
            TimeProvider.SetCurrentTime(Now2);
            Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 5, 90);
            TimeProvider.SetCurrentTime(Now3);

            // act
            var events = Book.CreateOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Buy, 3, 100);

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
            Assert.IsNull(matched.Fills[0].Order.TriggerPrice);
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
            Assert.IsNull(matched.Fills[1].Order.TriggerPrice);
            Assert.AreEqual(3, matched.Fills[1].Order.Quantity);
            Assert.AreEqual(3, matched.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(0, matched.Fills[1].Order.RemainingQuantity);
        }

        [Test]
        public void LimitOrder_MatchBuyAgainstOrdersAtSamePriceByTime()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Sell, 5, 90);
            TimeProvider.SetCurrentTime(Now2);
            Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 5, 90);
            TimeProvider.SetCurrentTime(Now3);

            // act
            var events = Book.CreateOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Buy, 8, 100);

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
            Assert.IsNull(matched1.Fills[0].Order.TriggerPrice);
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
            Assert.IsNull(matched1.Fills[1].Order.TriggerPrice);
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
            Assert.IsNull(matched2.Fills[0].Order.TriggerPrice);
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
            Assert.IsNull(matched2.Fills[1].Order.TriggerPrice);
            Assert.AreEqual(8, matched2.Fills[1].Order.Quantity);
            Assert.AreEqual(8, matched2.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(0, matched2.Fills[1].Order.RemainingQuantity);
        }

        [Test]
        public void LimitOrder_MatchAgainstOrderAtSamePriceByTimeAfterIncreaseQuantity()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 5, 110);
            TimeProvider.SetCurrentTime(Now2);
            Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Buy, 5, 110);
            TimeProvider.SetCurrentTime(Now3);
            Book.UpdateOrder(ClientId1, OrderId1, 7);
            TimeProvider.SetCurrentTime(Now4);

            // act
            var events = Book.CreateOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Sell, 3, 100);

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
            Assert.IsNull(matched.Fills[0].Order.TriggerPrice);
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
            Assert.IsNull(matched.Fills[1].Order.TriggerPrice);
            Assert.AreEqual(3, matched.Fills[1].Order.Quantity);
            Assert.AreEqual(3, matched.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(0, matched.Fills[1].Order.RemainingQuantity);
        }

        [Test]
        public void LimitOrder_MatchAgainstOrdersAtSamePriceByTimeAfterIncreaseQuantity()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 5, 110);
            TimeProvider.SetCurrentTime(Now2);
            Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Buy, 5, 110);
            TimeProvider.SetCurrentTime(Now3);
            Book.UpdateOrder(ClientId1, OrderId1, 6);
            TimeProvider.SetCurrentTime(Now4);

            // act
            var events = Book.CreateOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Sell, 8, 100);

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
            Assert.IsNull(matched1.Fills[0].Order.TriggerPrice);
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
            Assert.IsNull(matched1.Fills[1].Order.TriggerPrice);
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
            Assert.IsNull(matched2.Fills[0].Order.TriggerPrice);
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
            Assert.IsNull(matched2.Fills[1].Order.TriggerPrice);
            Assert.AreEqual(8, matched2.Fills[1].Order.Quantity);
            Assert.AreEqual(8, matched2.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(0, matched2.Fills[1].Order.RemainingQuantity);
        }

        [Test]
        public void LimitOrder_MatchAgainstOrderAtSamePriceByTimeAfterDecreaseQuantity()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 5, 110);
            TimeProvider.SetCurrentTime(Now2);
            Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Buy, 5, 110);
            TimeProvider.SetCurrentTime(Now3);
            Book.UpdateOrder(ClientId1, OrderId1, 4, 110);
            TimeProvider.SetCurrentTime(Now4);

            // act
            var events = Book.CreateOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Sell, 3, 100);

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
            Assert.IsNull(matched.Fills[0].Order.TriggerPrice);
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
            Assert.IsNull(matched.Fills[1].Order.TriggerPrice);
            Assert.AreEqual(3, matched.Fills[1].Order.Quantity);
            Assert.AreEqual(3, matched.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(0, matched.Fills[1].Order.RemainingQuantity);
        }

        [Test]
        public void LimitOrder_MatchAgainstOrdersAtSamePriceByTimeAfterDecreaseQuantity()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 5, 110);
            TimeProvider.SetCurrentTime(Now2);
            Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Buy, 5, 110);
            TimeProvider.SetCurrentTime(Now3);
            Book.UpdateOrder(ClientId1, OrderId1, 4, 110);
            TimeProvider.SetCurrentTime(Now4);

            // act
            var events = Book.CreateOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Sell, 8, 100);

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
            Assert.IsNull(matched1.Fills[0].Order.TriggerPrice);
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
            Assert.IsNull(matched1.Fills[1].Order.TriggerPrice);
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
            Assert.IsNull(matched2.Fills[0].Order.TriggerPrice);
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
            Assert.IsNull(matched2.Fills[1].Order.TriggerPrice);
            Assert.AreEqual(8, matched2.Fills[1].Order.Quantity);
            Assert.AreEqual(8, matched2.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(0, matched2.Fills[1].Order.RemainingQuantity);
        }

        [Test]
        public void LimitOrder_MatchGoodTilCanceledAfterReopen()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.GoodTilCanceled, Side.Buy, 5, 100);
            TimeProvider.SetCurrentTime(Now2);
            Book.UpdateStatus(OrderBookStatus.Closed);
            TimeProvider.SetCurrentTime(Now3);
            Book.UpdateStatus(OrderBookStatus.Open);
            TimeProvider.SetCurrentTime(Now4);

            // act
            var events = Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 8, 100);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(2, events.Count);

            var matched = events[1] as OrdersMatched;
            Assert.IsNotNull(matched);
            Assert.AreEqual(Sec, matched.Security);
            Assert.AreEqual(Now4, matched.Time);
            Assert.AreEqual(100, matched.Price);
            Assert.AreEqual(5, matched.Quantity);
            Assert.IsNotNull(matched.Fills);
            Assert.AreEqual(2, matched.Fills.Count);

            Assert.AreEqual(Sec, matched.Fills[0].Security);
            Assert.AreEqual(Now4, matched.Fills[0].Time);
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
            Assert.AreEqual(Now4, matched.Fills[0].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched.Fills[0].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched.Fills[0].Order.Type);
            Assert.AreEqual(OrderValidity.GoodTilCanceled, matched.Fills[0].Order.OrderValidity);
            Assert.AreEqual(Side.Buy, matched.Fills[0].Order.Side);
            Assert.AreEqual(100, matched.Fills[0].Order.Price);
            Assert.IsNull(matched.Fills[0].Order.TriggerPrice);
            Assert.AreEqual(5, matched.Fills[0].Order.Quantity);
            Assert.AreEqual(5, matched.Fills[0].Order.FilledQuantity);
            Assert.AreEqual(0, matched.Fills[0].Order.RemainingQuantity);

            Assert.AreEqual(Sec, matched.Fills[1].Security);
            Assert.AreEqual(Now4, matched.Fills[1].Time);
            Assert.AreEqual(ClientId2, matched.Fills[1].ClientId);
            Assert.AreEqual(OrderId2, matched.Fills[1].OrderId);
            Assert.AreEqual(100, matched.Fills[1].Price);
            Assert.AreEqual(5, matched.Fills[1].Quantity);
            Assert.AreEqual(false, matched.Fills[1].IsResting);
            Assert.AreEqual(ClientId2, matched.Fills[1].Order.ClientId);
            Assert.AreEqual(OrderId2, matched.Fills[1].Order.OrderId);
            Assert.AreEqual(Sec, matched.Fills[1].Order.Security);
            Assert.AreEqual(Now4, matched.Fills[1].Order.CreatedTime);
            Assert.AreEqual(Now4, matched.Fills[1].Order.ModifiedTime);
            Assert.IsNull(matched.Fills[1].Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched.Fills[1].Order.Status);
            Assert.AreEqual(OrderType.Limit, matched.Fills[1].Order.Type);
            Assert.AreEqual(OrderValidity.Day, matched.Fills[1].Order.OrderValidity);
            Assert.AreEqual(Side.Sell, matched.Fills[1].Order.Side);
            Assert.AreEqual(100, matched.Fills[1].Order.Price);
            Assert.IsNull(matched.Fills[1].Order.TriggerPrice);
            Assert.AreEqual(8, matched.Fills[1].Order.Quantity);
            Assert.AreEqual(5, matched.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(3, matched.Fills[1].Order.RemainingQuantity);
        }

        [Test]
        public void MarketClosed_Rejected()
        {
            // arrange
            // act
            var events = Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3, 100);

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

        [Test]
        public void MarketOrder_MarketPreOpen_Rejected()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.PreOpen);

            // act
            var events = Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3);

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
        public void InvalidQuantity_Rejected(int quantity)
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);

            // act
            var events = Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, quantity, 100);

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
        public void InvalidPriceIncrement_Rejected(decimal price)
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);

            // act
            var events = Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 6, price);

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

        [TestCase(8)]
        [TestCase(-8)]
        [TestCase(-108)]
        [TestCase(10.01)]
        public void InvalidTriggerPriceIncrement_Rejected(decimal triggerPrice)
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);

            // act
            var events = Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 6, null, triggerPrice);

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
        public void StopOrder_TriggerPriceMustBeLessThanPrice_Rejected()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);

            // act
            var events = Book.CreateOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Buy, 3, 90, 100);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as CreateOrderRejected;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(Sec, rejected.Security);
            Assert.AreEqual(Now1, rejected.Time);
            Assert.AreEqual(ClientId3, rejected.ClientId);
            Assert.AreEqual(OrderId3, rejected.OrderId);
            Assert.AreEqual(OrderRejectedReason.TriggerPriceMustBeLessThanPrice, rejected.Reason);
        }

        [Test]
        public void StopOrder_TriggerPriceMustBeGreaterThanPrice_Rejected()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);

            // act
            var events = Book.CreateOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Sell, 3, 110, 100);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as CreateOrderRejected;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(Sec, rejected.Security);
            Assert.AreEqual(Now1, rejected.Time);
            Assert.AreEqual(ClientId3, rejected.ClientId);
            Assert.AreEqual(OrderId3, rejected.OrderId);
            Assert.AreEqual(OrderRejectedReason.TriggerPriceMustBeGreaterThanPrice, rejected.Reason);
        }
        
        [Test]
        public void StopOrder_NoLastTradedPrice_Rejected()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);

            // act
            var events = Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3, null, 100);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as CreateOrderRejected;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(Sec, rejected.Security);
            Assert.AreEqual(Now1, rejected.Time);
            Assert.AreEqual(ClientId1, rejected.ClientId);
            Assert.AreEqual(OrderId1, rejected.OrderId);
            Assert.AreEqual(OrderRejectedReason.NoLastTradedPrice, rejected.Reason);
        }

        [TestCase(90)]
        [TestCase(100)]
        public void StopOrder_TriggerPriceMustBeGreaterThanLastTraded_Rejected(int price)
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3, 100);
            Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 3, 100);

            // act
            var events = Book.CreateOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Buy, 3, null, price);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as CreateOrderRejected;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(Sec, rejected.Security);
            Assert.AreEqual(Now1, rejected.Time);
            Assert.AreEqual(ClientId3, rejected.ClientId);
            Assert.AreEqual(OrderId3, rejected.OrderId);
            Assert.AreEqual(OrderRejectedReason.TriggerPriceMustBeGreaterThanLastTradedPrice, rejected.Reason);
        }

        [TestCase(110)]
        [TestCase(100)]
        public void StopOrder_TriggerPriceMustBeLessThanLastTraded_Rejected(int price)
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3, 100);
            Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 3, 100);

            // act
            var events = Book.CreateOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Sell, 3, null, price);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as CreateOrderRejected;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(Sec, rejected.Security);
            Assert.AreEqual(Now1, rejected.Time);
            Assert.AreEqual(ClientId3, rejected.ClientId);
            Assert.AreEqual(OrderId3, rejected.OrderId);
            Assert.AreEqual(OrderRejectedReason.TriggerPriceMustBeLessThanLastTradedPrice, rejected.Reason);
        }
        
        [Test]
        public void MarketOrder_EmptyBook_Rejected()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);

            // act
            var events = Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3);

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
        
        [Test]
        public void DuplicateOrderId_Rejected()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3, 100);
            
            // act
            var events = Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Sell, 5, 100);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as CreateOrderRejected;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(Sec, rejected.Security);
            Assert.AreEqual(Now1, rejected.Time);
            Assert.AreEqual(ClientId1, rejected.ClientId);
            Assert.AreEqual(OrderId1, rejected.OrderId);
            Assert.AreEqual(OrderRejectedReason.OrderInBook, rejected.Reason);
        }

    }
}