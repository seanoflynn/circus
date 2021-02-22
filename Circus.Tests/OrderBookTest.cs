using System;
using System.Linq;
using Circus.OrderBook;
using NUnit.Framework;

namespace Circus.Tests
{
    [TestFixture]
    public class OrderBookTest
    {
        [Test]
        public void CreateLimitOrder_Valid_Success()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now);
            var book = new OrderBook.OrderBook(sec, timeProvider);

            book.SetStatus(OrderBookStatus.Open);
            var id = Guid.NewGuid();

            // act
            var events = book.CreateLimitOrder(id, TimeInForce.Day, Side.Buy, 100, 3).ToList();

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);

            var created = events[0] as OrderCreatedEvent;
            Assert.IsNotNull(created);
            Assert.AreEqual(id, created.Order.Id);
            Assert.AreEqual(sec, created.Order.Security);
            Assert.AreEqual(now, created.Order.CreatedTime);
            Assert.AreEqual(now, created.Order.ModifiedTime);
            Assert.IsNull(created.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, created.Order.Status);
            Assert.AreEqual(OrderType.Limit, created.Order.Type);
            Assert.AreEqual(TimeInForce.Day, created.Order.TimeInForce);
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
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now1 = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now1);
            var book = new OrderBook.OrderBook(sec, timeProvider);
            book.SetStatus(OrderBookStatus.Open);

            var id1 = Guid.NewGuid();
            book.CreateLimitOrder(id1, TimeInForce.Day, Side.Buy, 100, 3);
            var now2 = new DateTime(2000, 1, 1, 12, 1, 0);
            timeProvider.SetCurrentTime(now2);
            var id2 = Guid.NewGuid();

            // act
            var events = book.CreateLimitOrder(id2, TimeInForce.Day, Side.Sell, 100, 5).ToList();

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(2, events.Count);

            var matched = events[1] as OrderMatchedEvent;
            Assert.IsNotNull(matched);
            Assert.AreEqual(now2, matched.Fill.Time);
            Assert.AreEqual(100, matched.Fill.Price);
            Assert.AreEqual(3, matched.Fill.Quantity);

            Assert.AreEqual(id1, matched.Resting.Id);
            Assert.AreEqual(sec, matched.Resting.Security);
            Assert.AreEqual(now1, matched.Resting.CreatedTime);
            Assert.AreEqual(now1, matched.Resting.ModifiedTime);
            Assert.AreEqual(now2, matched.Resting.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched.Resting.Status);
            Assert.AreEqual(OrderType.Limit, matched.Resting.Type);
            Assert.AreEqual(TimeInForce.Day, matched.Resting.TimeInForce);
            Assert.AreEqual(Side.Buy, matched.Resting.Side);
            Assert.AreEqual(100, matched.Resting.Price);
            Assert.IsNull(matched.Resting.StopPrice);
            Assert.AreEqual(3, matched.Resting.Quantity);
            Assert.AreEqual(3, matched.Resting.FilledQuantity);
            Assert.AreEqual(0, matched.Resting.RemainingQuantity);

            Assert.AreEqual(id2, matched.Aggressor.Id);
            Assert.AreEqual(sec, matched.Aggressor.Security);
            Assert.AreEqual(now2, matched.Aggressor.CreatedTime);
            Assert.AreEqual(now2, matched.Aggressor.ModifiedTime);
            Assert.IsNull(matched.Aggressor.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched.Aggressor.Status);
            Assert.AreEqual(OrderType.Limit, matched.Aggressor.Type);
            Assert.AreEqual(TimeInForce.Day, matched.Aggressor.TimeInForce);
            Assert.AreEqual(Side.Sell, matched.Aggressor.Side);
            Assert.AreEqual(100, matched.Aggressor.Price);
            Assert.IsNull(matched.Aggressor.StopPrice);
            Assert.AreEqual(5, matched.Aggressor.Quantity);
            Assert.AreEqual(3, matched.Aggressor.FilledQuantity);
            Assert.AreEqual(2, matched.Aggressor.RemainingQuantity);
        }

        [Test]
        public void CreateLimitOrder_MatchAtDifferentPriceWithAggressorRemaining_Success()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now1 = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now1);
            var book = new OrderBook.OrderBook(sec, timeProvider);
            book.SetStatus(OrderBookStatus.Open);

            var id1 = Guid.NewGuid();
            book.CreateLimitOrder(id1, TimeInForce.Day, Side.Buy, 110, 3);
            var now2 = new DateTime(2000, 1, 1, 12, 1, 0);
            timeProvider.SetCurrentTime(now2);
            var id2 = Guid.NewGuid();

            // act
            var events = book.CreateLimitOrder(id2, TimeInForce.Day, Side.Sell, 100, 5).ToList();

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(2, events.Count);

            var matched = events[1] as OrderMatchedEvent;
            Assert.IsNotNull(matched);
            Assert.AreEqual(now2, matched.Fill.Time);
            Assert.AreEqual(110, matched.Fill.Price);
            Assert.AreEqual(3, matched.Fill.Quantity);

            Assert.AreEqual(id1, matched.Resting.Id);
            Assert.AreEqual(sec, matched.Resting.Security);
            Assert.AreEqual(now1, matched.Resting.CreatedTime);
            Assert.AreEqual(now1, matched.Resting.ModifiedTime);
            Assert.AreEqual(now2, matched.Resting.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched.Resting.Status);
            Assert.AreEqual(OrderType.Limit, matched.Resting.Type);
            Assert.AreEqual(TimeInForce.Day, matched.Resting.TimeInForce);
            Assert.AreEqual(Side.Buy, matched.Resting.Side);
            Assert.AreEqual(110, matched.Resting.Price);
            Assert.IsNull(matched.Resting.StopPrice);
            Assert.AreEqual(3, matched.Resting.Quantity);
            Assert.AreEqual(3, matched.Resting.FilledQuantity);
            Assert.AreEqual(0, matched.Resting.RemainingQuantity);

            Assert.IsNotNull(matched.Aggressor);
            Assert.AreEqual(id2, matched.Aggressor.Id);
            Assert.AreEqual(sec, matched.Aggressor.Security);
            Assert.AreEqual(now2, matched.Aggressor.CreatedTime);
            Assert.AreEqual(now2, matched.Aggressor.ModifiedTime);
            Assert.IsNull(matched.Aggressor.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched.Aggressor.Status);
            Assert.AreEqual(OrderType.Limit, matched.Aggressor.Type);
            Assert.AreEqual(TimeInForce.Day, matched.Aggressor.TimeInForce);
            Assert.AreEqual(Side.Sell, matched.Aggressor.Side);
            Assert.AreEqual(100, matched.Aggressor.Price);
            Assert.IsNull(matched.Aggressor.StopPrice);
            Assert.AreEqual(5, matched.Aggressor.Quantity);
            Assert.AreEqual(3, matched.Aggressor.FilledQuantity);
            Assert.AreEqual(2, matched.Aggressor.RemainingQuantity);
        }

        [Test]
        public void CreateLimitOrder_MatchAtDifferentPriceWithRestingRemaining_Success()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now1 = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now1);
            var book = new OrderBook.OrderBook(sec, timeProvider);
            book.SetStatus(OrderBookStatus.Open);

            var id1 = Guid.NewGuid();
            book.CreateLimitOrder(id1, TimeInForce.Day, Side.Buy, 110, 5);
            var now2 = new DateTime(2000, 1, 1, 12, 1, 0);
            timeProvider.SetCurrentTime(now2);
            var id2 = Guid.NewGuid();

            // act
            var events = book.CreateLimitOrder(id2, TimeInForce.Day, Side.Sell, 100, 3).ToList();

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(2, events.Count);

            var matched = events[1] as OrderMatchedEvent;
            Assert.IsNotNull(matched);
            Assert.AreEqual(now2, matched.Fill.Time);
            Assert.AreEqual(110, matched.Fill.Price);
            Assert.AreEqual(3, matched.Fill.Quantity);

            Assert.AreEqual(id1, matched.Resting.Id);
            Assert.AreEqual(sec, matched.Resting.Security);
            Assert.AreEqual(now1, matched.Resting.CreatedTime);
            Assert.AreEqual(now1, matched.Resting.ModifiedTime);
            Assert.IsNull(matched.Resting.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched.Resting.Status);
            Assert.AreEqual(OrderType.Limit, matched.Resting.Type);
            Assert.AreEqual(TimeInForce.Day, matched.Resting.TimeInForce);
            Assert.AreEqual(Side.Buy, matched.Resting.Side);
            Assert.AreEqual(110, matched.Resting.Price);
            Assert.IsNull(matched.Resting.StopPrice);
            Assert.AreEqual(5, matched.Resting.Quantity);
            Assert.AreEqual(3, matched.Resting.FilledQuantity);
            Assert.AreEqual(2, matched.Resting.RemainingQuantity);

            Assert.AreEqual(id2, matched.Aggressor.Id);
            Assert.AreEqual(sec, matched.Aggressor.Security);
            Assert.AreEqual(now2, matched.Aggressor.CreatedTime);
            Assert.AreEqual(now2, matched.Aggressor.ModifiedTime);
            Assert.AreEqual(now2, matched.Aggressor.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched.Aggressor.Status);
            Assert.AreEqual(OrderType.Limit, matched.Aggressor.Type);
            Assert.AreEqual(TimeInForce.Day, matched.Aggressor.TimeInForce);
            Assert.AreEqual(Side.Sell, matched.Aggressor.Side);
            Assert.AreEqual(100, matched.Aggressor.Price);
            Assert.IsNull(matched.Aggressor.StopPrice);
            Assert.AreEqual(3, matched.Aggressor.Quantity);
            Assert.AreEqual(3, matched.Aggressor.FilledQuantity);
            Assert.AreEqual(0, matched.Aggressor.RemainingQuantity);
        }

        [Test]
        public void CreateLimitOrder_MatchSellAgainstOrderByTime()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now1 = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now1);
            var book = new OrderBook.OrderBook(sec, timeProvider);
            book.SetStatus(OrderBookStatus.Open);

            var id1 = Guid.NewGuid();
            book.CreateLimitOrder(id1, TimeInForce.Day, Side.Buy, 110, 5);
            var now2 = new DateTime(2000, 1, 1, 12, 1, 0);
            timeProvider.SetCurrentTime(now2);
            var id2 = Guid.NewGuid();
            book.CreateLimitOrder(id2, TimeInForce.Day, Side.Buy, 120, 5);
            var now3 = new DateTime(2000, 1, 1, 12, 2, 0);
            timeProvider.SetCurrentTime(now3);
            var id3 = Guid.NewGuid();

            // act
            var events = book.CreateLimitOrder(id3, TimeInForce.Day, Side.Sell, 100, 3).ToList();

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(2, events.Count);

            var matched = events[1] as OrderMatchedEvent;
            Assert.IsNotNull(matched);
            Assert.AreEqual(now3, matched.Fill.Time);
            Assert.AreEqual(120, matched.Fill.Price);
            Assert.AreEqual(3, matched.Fill.Quantity);

            Assert.AreEqual(id2, matched.Resting.Id);
            Assert.AreEqual(sec, matched.Resting.Security);
            Assert.AreEqual(now2, matched.Resting.CreatedTime);
            Assert.AreEqual(now2, matched.Resting.ModifiedTime);
            Assert.IsNull(matched.Resting.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched.Resting.Status);
            Assert.AreEqual(OrderType.Limit, matched.Resting.Type);
            Assert.AreEqual(TimeInForce.Day, matched.Resting.TimeInForce);
            Assert.AreEqual(Side.Buy, matched.Resting.Side);
            Assert.AreEqual(120, matched.Resting.Price);
            Assert.IsNull(matched.Resting.StopPrice);
            Assert.AreEqual(5, matched.Resting.Quantity);
            Assert.AreEqual(3, matched.Resting.FilledQuantity);
            Assert.AreEqual(2, matched.Resting.RemainingQuantity);

            Assert.AreEqual(id3, matched.Aggressor.Id);
            Assert.AreEqual(sec, matched.Aggressor.Security);
            Assert.AreEqual(now3, matched.Aggressor.CreatedTime);
            Assert.AreEqual(now3, matched.Aggressor.ModifiedTime);
            Assert.AreEqual(now3, matched.Aggressor.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched.Aggressor.Status);
            Assert.AreEqual(OrderType.Limit, matched.Aggressor.Type);
            Assert.AreEqual(TimeInForce.Day, matched.Aggressor.TimeInForce);
            Assert.AreEqual(Side.Sell, matched.Aggressor.Side);
            Assert.AreEqual(100, matched.Aggressor.Price);
            Assert.IsNull(matched.Aggressor.StopPrice);
            Assert.AreEqual(3, matched.Aggressor.Quantity);
            Assert.AreEqual(3, matched.Aggressor.FilledQuantity);
            Assert.AreEqual(0, matched.Aggressor.RemainingQuantity);
        }

        [Test]
        public void CreateLimitOrder_MatchSellAgainstOrdersByPrice()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now1 = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now1);
            var book = new OrderBook.OrderBook(sec, timeProvider);
            book.SetStatus(OrderBookStatus.Open);

            var id1 = Guid.NewGuid();
            book.CreateLimitOrder(id1, TimeInForce.Day, Side.Buy, 110, 5);
            var now2 = new DateTime(2000, 1, 1, 12, 1, 0);
            timeProvider.SetCurrentTime(now2);
            var id2 = Guid.NewGuid();
            book.CreateLimitOrder(id2, TimeInForce.Day, Side.Buy, 120, 5);
            var now3 = new DateTime(2000, 1, 1, 12, 2, 0);
            timeProvider.SetCurrentTime(now3);
            var id3 = Guid.NewGuid();

            // act
            var events = book.CreateLimitOrder(id3, TimeInForce.Day, Side.Sell, 100, 8).ToList();

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(3, events.Count);

            var matched1 = events[1] as OrderMatchedEvent;
            Assert.IsNotNull(matched1);
            Assert.AreEqual(now3, matched1.Fill.Time);
            Assert.AreEqual(120, matched1.Fill.Price);
            Assert.AreEqual(5, matched1.Fill.Quantity);

            Assert.AreEqual(id2, matched1.Resting.Id);
            Assert.AreEqual(sec, matched1.Resting.Security);
            Assert.AreEqual(now2, matched1.Resting.CreatedTime);
            Assert.AreEqual(now2, matched1.Resting.ModifiedTime);
            Assert.AreEqual(now3, matched1.Resting.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched1.Resting.Status);
            Assert.AreEqual(OrderType.Limit, matched1.Resting.Type);
            Assert.AreEqual(TimeInForce.Day, matched1.Resting.TimeInForce);
            Assert.AreEqual(Side.Buy, matched1.Resting.Side);
            Assert.AreEqual(120, matched1.Resting.Price);
            Assert.IsNull(matched1.Resting.StopPrice);
            Assert.AreEqual(5, matched1.Resting.Quantity);
            Assert.AreEqual(5, matched1.Resting.FilledQuantity);
            Assert.AreEqual(0, matched1.Resting.RemainingQuantity);

            Assert.AreEqual(id3, matched1.Aggressor.Id);
            Assert.AreEqual(sec, matched1.Aggressor.Security);
            Assert.AreEqual(now3, matched1.Aggressor.CreatedTime);
            Assert.AreEqual(now3, matched1.Aggressor.ModifiedTime);
            Assert.IsNull(matched1.Aggressor.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched1.Aggressor.Status);
            Assert.AreEqual(OrderType.Limit, matched1.Aggressor.Type);
            Assert.AreEqual(TimeInForce.Day, matched1.Aggressor.TimeInForce);
            Assert.AreEqual(Side.Sell, matched1.Aggressor.Side);
            Assert.AreEqual(100, matched1.Aggressor.Price);
            Assert.IsNull(matched1.Aggressor.StopPrice);
            Assert.AreEqual(8, matched1.Aggressor.Quantity);
            Assert.AreEqual(5, matched1.Aggressor.FilledQuantity);
            Assert.AreEqual(3, matched1.Aggressor.RemainingQuantity);

            var matched2 = events[2] as OrderMatchedEvent;
            Assert.IsNotNull(matched2);
            Assert.AreEqual(now3, matched2.Fill.Time);
            Assert.AreEqual(110, matched2.Fill.Price);
            Assert.AreEqual(3, matched2.Fill.Quantity);

            Assert.AreEqual(id1, matched2.Resting.Id);
            Assert.AreEqual(sec, matched2.Resting.Security);
            Assert.AreEqual(now1, matched2.Resting.CreatedTime);
            Assert.AreEqual(now1, matched2.Resting.ModifiedTime);
            Assert.IsNull(matched2.Resting.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched2.Resting.Status);
            Assert.AreEqual(OrderType.Limit, matched2.Resting.Type);
            Assert.AreEqual(TimeInForce.Day, matched2.Resting.TimeInForce);
            Assert.AreEqual(Side.Buy, matched2.Resting.Side);
            Assert.AreEqual(110, matched2.Resting.Price);
            Assert.IsNull(matched2.Resting.StopPrice);
            Assert.AreEqual(5, matched2.Resting.Quantity);
            Assert.AreEqual(3, matched2.Resting.FilledQuantity);
            Assert.AreEqual(2, matched2.Resting.RemainingQuantity);

            Assert.AreEqual(id3, matched2.Aggressor.Id);
            Assert.AreEqual(sec, matched2.Aggressor.Security);
            Assert.AreEqual(now3, matched2.Aggressor.CreatedTime);
            Assert.AreEqual(now3, matched2.Aggressor.ModifiedTime);
            Assert.AreEqual(now3, matched2.Aggressor.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched2.Aggressor.Status);
            Assert.AreEqual(OrderType.Limit, matched2.Aggressor.Type);
            Assert.AreEqual(TimeInForce.Day, matched2.Aggressor.TimeInForce);
            Assert.AreEqual(Side.Sell, matched2.Aggressor.Side);
            Assert.AreEqual(100, matched2.Aggressor.Price);
            Assert.IsNull(matched2.Aggressor.StopPrice);
            Assert.AreEqual(8, matched2.Aggressor.Quantity);
            Assert.AreEqual(8, matched2.Aggressor.FilledQuantity);
            Assert.AreEqual(0, matched2.Aggressor.RemainingQuantity);
        }

        [Test]
        public void CreateLimitOrder_MatchSellAgainstOrderAtSamePriceByTime()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now1 = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now1);
            var book = new OrderBook.OrderBook(sec, timeProvider);
            book.SetStatus(OrderBookStatus.Open);

            var id1 = Guid.NewGuid();
            book.CreateLimitOrder(id1, TimeInForce.Day, Side.Buy, 110, 5);
            var now2 = new DateTime(2000, 1, 1, 12, 1, 0);
            timeProvider.SetCurrentTime(now2);
            var id2 = Guid.NewGuid();
            book.CreateLimitOrder(id2, TimeInForce.Day, Side.Buy, 110, 5);
            var now3 = new DateTime(2000, 1, 1, 12, 2, 0);
            timeProvider.SetCurrentTime(now3);
            var id3 = Guid.NewGuid();

            // act
            var events = book.CreateLimitOrder(id3, TimeInForce.Day, Side.Sell, 100, 3).ToList();

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(2, events.Count);

            var matched = events[1] as OrderMatchedEvent;
            Assert.IsNotNull(matched);
            Assert.AreEqual(now3, matched.Fill.Time);
            Assert.AreEqual(110, matched.Fill.Price);
            Assert.AreEqual(3, matched.Fill.Quantity);

            Assert.AreEqual(id1, matched.Resting.Id);
            Assert.AreEqual(sec, matched.Resting.Security);
            Assert.AreEqual(now1, matched.Resting.CreatedTime);
            Assert.AreEqual(now1, matched.Resting.ModifiedTime);
            Assert.IsNull(matched.Resting.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched.Resting.Status);
            Assert.AreEqual(OrderType.Limit, matched.Resting.Type);
            Assert.AreEqual(TimeInForce.Day, matched.Resting.TimeInForce);
            Assert.AreEqual(Side.Buy, matched.Resting.Side);
            Assert.AreEqual(110, matched.Resting.Price);
            Assert.IsNull(matched.Resting.StopPrice);
            Assert.AreEqual(5, matched.Resting.Quantity);
            Assert.AreEqual(3, matched.Resting.FilledQuantity);
            Assert.AreEqual(2, matched.Resting.RemainingQuantity);

            Assert.AreEqual(id3, matched.Aggressor.Id);
            Assert.AreEqual(sec, matched.Aggressor.Security);
            Assert.AreEqual(now3, matched.Aggressor.CreatedTime);
            Assert.AreEqual(now3, matched.Aggressor.ModifiedTime);
            Assert.AreEqual(now3, matched.Aggressor.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched.Aggressor.Status);
            Assert.AreEqual(OrderType.Limit, matched.Aggressor.Type);
            Assert.AreEqual(TimeInForce.Day, matched.Aggressor.TimeInForce);
            Assert.AreEqual(Side.Sell, matched.Aggressor.Side);
            Assert.AreEqual(100, matched.Aggressor.Price);
            Assert.IsNull(matched.Aggressor.StopPrice);
            Assert.AreEqual(3, matched.Aggressor.Quantity);
            Assert.AreEqual(3, matched.Aggressor.FilledQuantity);
            Assert.AreEqual(0, matched.Aggressor.RemainingQuantity);
        }

        [Test]
        public void CreateLimitOrder_MatchSellAgainstOrdersAtSamePriceByTime()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now1 = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now1);
            var book = new OrderBook.OrderBook(sec, timeProvider);
            book.SetStatus(OrderBookStatus.Open);

            var id1 = Guid.NewGuid();
            book.CreateLimitOrder(id1, TimeInForce.Day, Side.Buy, 110, 5);
            var now2 = new DateTime(2000, 1, 1, 12, 1, 0);
            timeProvider.SetCurrentTime(now2);
            var id2 = Guid.NewGuid();
            book.CreateLimitOrder(id2, TimeInForce.Day, Side.Buy, 110, 5);
            var now3 = new DateTime(2000, 1, 1, 12, 2, 0);
            timeProvider.SetCurrentTime(now3);
            var id3 = Guid.NewGuid();

            // act
            var events = book.CreateLimitOrder(id3, TimeInForce.Day, Side.Sell, 100, 8).ToList();

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(3, events.Count);

            var matched1 = events[1] as OrderMatchedEvent;
            Assert.IsNotNull(matched1);
            Assert.AreEqual(now3, matched1.Fill.Time);
            Assert.AreEqual(110, matched1.Fill.Price);
            Assert.AreEqual(5, matched1.Fill.Quantity);

            Assert.AreEqual(id1, matched1.Resting.Id);
            Assert.AreEqual(sec, matched1.Resting.Security);
            Assert.AreEqual(now1, matched1.Resting.CreatedTime);
            Assert.AreEqual(now1, matched1.Resting.ModifiedTime);
            Assert.AreEqual(now3, matched1.Resting.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched1.Resting.Status);
            Assert.AreEqual(OrderType.Limit, matched1.Resting.Type);
            Assert.AreEqual(TimeInForce.Day, matched1.Resting.TimeInForce);
            Assert.AreEqual(Side.Buy, matched1.Resting.Side);
            Assert.AreEqual(110, matched1.Resting.Price);
            Assert.IsNull(matched1.Resting.StopPrice);
            Assert.AreEqual(5, matched1.Resting.Quantity);
            Assert.AreEqual(5, matched1.Resting.FilledQuantity);
            Assert.AreEqual(0, matched1.Resting.RemainingQuantity);

            Assert.AreEqual(id3, matched1.Aggressor.Id);
            Assert.AreEqual(sec, matched1.Aggressor.Security);
            Assert.AreEqual(now3, matched1.Aggressor.CreatedTime);
            Assert.AreEqual(now3, matched1.Aggressor.ModifiedTime);
            Assert.IsNull(matched1.Aggressor.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched1.Aggressor.Status);
            Assert.AreEqual(OrderType.Limit, matched1.Aggressor.Type);
            Assert.AreEqual(TimeInForce.Day, matched1.Aggressor.TimeInForce);
            Assert.AreEqual(Side.Sell, matched1.Aggressor.Side);
            Assert.AreEqual(100, matched1.Aggressor.Price);
            Assert.IsNull(matched1.Aggressor.StopPrice);
            Assert.AreEqual(8, matched1.Aggressor.Quantity);
            Assert.AreEqual(5, matched1.Aggressor.FilledQuantity);
            Assert.AreEqual(3, matched1.Aggressor.RemainingQuantity);

            var matched2 = events[2] as OrderMatchedEvent;
            Assert.IsNotNull(matched2);
            Assert.AreEqual(now3, matched2.Fill.Time);
            Assert.AreEqual(110, matched2.Fill.Price);
            Assert.AreEqual(3, matched2.Fill.Quantity);

            Assert.AreEqual(id2, matched2.Resting.Id);
            Assert.AreEqual(sec, matched2.Resting.Security);
            Assert.AreEqual(now2, matched2.Resting.CreatedTime);
            Assert.AreEqual(now2, matched2.Resting.ModifiedTime);
            Assert.IsNull(matched2.Resting.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched2.Resting.Status);
            Assert.AreEqual(OrderType.Limit, matched2.Resting.Type);
            Assert.AreEqual(TimeInForce.Day, matched2.Resting.TimeInForce);
            Assert.AreEqual(Side.Buy, matched2.Resting.Side);
            Assert.AreEqual(110, matched2.Resting.Price);
            Assert.IsNull(matched2.Resting.StopPrice);
            Assert.AreEqual(5, matched2.Resting.Quantity);
            Assert.AreEqual(3, matched2.Resting.FilledQuantity);
            Assert.AreEqual(2, matched2.Resting.RemainingQuantity);

            Assert.AreEqual(id3, matched2.Aggressor.Id);
            Assert.AreEqual(sec, matched2.Aggressor.Security);
            Assert.AreEqual(now3, matched2.Aggressor.CreatedTime);
            Assert.AreEqual(now3, matched2.Aggressor.ModifiedTime);
            Assert.AreEqual(now3, matched2.Aggressor.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched2.Aggressor.Status);
            Assert.AreEqual(OrderType.Limit, matched2.Aggressor.Type);
            Assert.AreEqual(TimeInForce.Day, matched2.Aggressor.TimeInForce);
            Assert.AreEqual(Side.Sell, matched2.Aggressor.Side);
            Assert.AreEqual(100, matched2.Aggressor.Price);
            Assert.IsNull(matched2.Aggressor.StopPrice);
            Assert.AreEqual(8, matched2.Aggressor.Quantity);
            Assert.AreEqual(8, matched2.Aggressor.FilledQuantity);
            Assert.AreEqual(0, matched2.Aggressor.RemainingQuantity);
        }

        [Test]
        public void CreateLimitOrder_MatchBuyAgainstOrderByTime()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now1 = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now1);
            var book = new OrderBook.OrderBook(sec, timeProvider);
            book.SetStatus(OrderBookStatus.Open);

            var id1 = Guid.NewGuid();
            book.CreateLimitOrder(id1, TimeInForce.Day, Side.Sell, 90, 5);
            var now2 = new DateTime(2000, 1, 1, 12, 1, 0);
            timeProvider.SetCurrentTime(now2);
            var id2 = Guid.NewGuid();
            book.CreateLimitOrder(id2, TimeInForce.Day, Side.Sell, 80, 5);
            var now3 = new DateTime(2000, 1, 1, 12, 2, 0);
            timeProvider.SetCurrentTime(now3);
            var id3 = Guid.NewGuid();

            // act
            var events = book.CreateLimitOrder(id3, TimeInForce.Day, Side.Buy, 100, 3).ToList();

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(2, events.Count);

            var matched = events[1] as OrderMatchedEvent;
            Assert.IsNotNull(matched);
            Assert.AreEqual(now3, matched.Fill.Time);
            Assert.AreEqual(80, matched.Fill.Price);
            Assert.AreEqual(3, matched.Fill.Quantity);

            Assert.AreEqual(id2, matched.Resting.Id);
            Assert.AreEqual(sec, matched.Resting.Security);
            Assert.AreEqual(now2, matched.Resting.CreatedTime);
            Assert.AreEqual(now2, matched.Resting.ModifiedTime);
            Assert.IsNull(matched.Resting.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched.Resting.Status);
            Assert.AreEqual(OrderType.Limit, matched.Resting.Type);
            Assert.AreEqual(TimeInForce.Day, matched.Resting.TimeInForce);
            Assert.AreEqual(Side.Sell, matched.Resting.Side);
            Assert.AreEqual(80, matched.Resting.Price);
            Assert.IsNull(matched.Resting.StopPrice);
            Assert.AreEqual(5, matched.Resting.Quantity);
            Assert.AreEqual(3, matched.Resting.FilledQuantity);
            Assert.AreEqual(2, matched.Resting.RemainingQuantity);

            Assert.AreEqual(id3, matched.Aggressor.Id);
            Assert.AreEqual(sec, matched.Aggressor.Security);
            Assert.AreEqual(now3, matched.Aggressor.CreatedTime);
            Assert.AreEqual(now3, matched.Aggressor.ModifiedTime);
            Assert.AreEqual(now3, matched.Aggressor.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched.Aggressor.Status);
            Assert.AreEqual(OrderType.Limit, matched.Aggressor.Type);
            Assert.AreEqual(TimeInForce.Day, matched.Aggressor.TimeInForce);
            Assert.AreEqual(Side.Buy, matched.Aggressor.Side);
            Assert.AreEqual(100, matched.Aggressor.Price);
            Assert.IsNull(matched.Aggressor.StopPrice);
            Assert.AreEqual(3, matched.Aggressor.Quantity);
            Assert.AreEqual(3, matched.Aggressor.FilledQuantity);
            Assert.AreEqual(0, matched.Aggressor.RemainingQuantity);
        }

        [Test]
        public void CreateLimitOrder_MatchBuyAgainstOrdersByPrice()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now1 = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now1);
            var book = new OrderBook.OrderBook(sec, timeProvider);
            book.SetStatus(OrderBookStatus.Open);

            var id1 = Guid.NewGuid();
            book.CreateLimitOrder(id1, TimeInForce.Day, Side.Sell, 90, 5);
            var now2 = new DateTime(2000, 1, 1, 12, 1, 0);
            timeProvider.SetCurrentTime(now2);
            var id2 = Guid.NewGuid();
            book.CreateLimitOrder(id2, TimeInForce.Day, Side.Sell, 80, 5);
            var now3 = new DateTime(2000, 1, 1, 12, 2, 0);
            timeProvider.SetCurrentTime(now3);
            var id3 = Guid.NewGuid();

            // act
            var events = book.CreateLimitOrder(id3, TimeInForce.Day, Side.Buy, 100, 8).ToList();

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(3, events.Count);

            var matched1 = events[1] as OrderMatchedEvent;
            Assert.IsNotNull(matched1);
            Assert.AreEqual(now3, matched1.Fill.Time);
            Assert.AreEqual(80, matched1.Fill.Price);
            Assert.AreEqual(5, matched1.Fill.Quantity);

            Assert.AreEqual(id2, matched1.Resting.Id);
            Assert.AreEqual(sec, matched1.Resting.Security);
            Assert.AreEqual(now2, matched1.Resting.CreatedTime);
            Assert.AreEqual(now2, matched1.Resting.ModifiedTime);
            Assert.AreEqual(now3, matched1.Resting.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched1.Resting.Status);
            Assert.AreEqual(OrderType.Limit, matched1.Resting.Type);
            Assert.AreEqual(TimeInForce.Day, matched1.Resting.TimeInForce);
            Assert.AreEqual(Side.Sell, matched1.Resting.Side);
            Assert.AreEqual(80, matched1.Resting.Price);
            Assert.IsNull(matched1.Resting.StopPrice);
            Assert.AreEqual(5, matched1.Resting.Quantity);
            Assert.AreEqual(5, matched1.Resting.FilledQuantity);
            Assert.AreEqual(0, matched1.Resting.RemainingQuantity);

            Assert.AreEqual(id3, matched1.Aggressor.Id);
            Assert.AreEqual(sec, matched1.Aggressor.Security);
            Assert.AreEqual(now3, matched1.Aggressor.CreatedTime);
            Assert.AreEqual(now3, matched1.Aggressor.ModifiedTime);
            Assert.IsNull(matched1.Aggressor.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched1.Aggressor.Status);
            Assert.AreEqual(OrderType.Limit, matched1.Aggressor.Type);
            Assert.AreEqual(TimeInForce.Day, matched1.Aggressor.TimeInForce);
            Assert.AreEqual(Side.Buy, matched1.Aggressor.Side);
            Assert.AreEqual(100, matched1.Aggressor.Price);
            Assert.IsNull(matched1.Aggressor.StopPrice);
            Assert.AreEqual(8, matched1.Aggressor.Quantity);
            Assert.AreEqual(5, matched1.Aggressor.FilledQuantity);
            Assert.AreEqual(3, matched1.Aggressor.RemainingQuantity);

            var matched2 = events[2] as OrderMatchedEvent;
            Assert.IsNotNull(matched2);
            Assert.AreEqual(now3, matched2.Fill.Time);
            Assert.AreEqual(90, matched2.Fill.Price);
            Assert.AreEqual(3, matched2.Fill.Quantity);

            Assert.AreEqual(id1, matched2.Resting.Id);
            Assert.AreEqual(sec, matched2.Resting.Security);
            Assert.AreEqual(now1, matched2.Resting.CreatedTime);
            Assert.AreEqual(now1, matched2.Resting.ModifiedTime);
            Assert.IsNull(matched2.Resting.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched2.Resting.Status);
            Assert.AreEqual(OrderType.Limit, matched2.Resting.Type);
            Assert.AreEqual(TimeInForce.Day, matched2.Resting.TimeInForce);
            Assert.AreEqual(Side.Sell, matched2.Resting.Side);
            Assert.AreEqual(90, matched2.Resting.Price);
            Assert.IsNull(matched2.Resting.StopPrice);
            Assert.AreEqual(5, matched2.Resting.Quantity);
            Assert.AreEqual(3, matched2.Resting.FilledQuantity);
            Assert.AreEqual(2, matched2.Resting.RemainingQuantity);

            Assert.AreEqual(id3, matched2.Aggressor.Id);
            Assert.AreEqual(sec, matched2.Aggressor.Security);
            Assert.AreEqual(now3, matched2.Aggressor.CreatedTime);
            Assert.AreEqual(now3, matched2.Aggressor.ModifiedTime);
            Assert.AreEqual(now3, matched2.Aggressor.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched2.Aggressor.Status);
            Assert.AreEqual(OrderType.Limit, matched2.Aggressor.Type);
            Assert.AreEqual(TimeInForce.Day, matched2.Aggressor.TimeInForce);
            Assert.AreEqual(Side.Buy, matched2.Aggressor.Side);
            Assert.AreEqual(100, matched2.Aggressor.Price);
            Assert.IsNull(matched2.Aggressor.StopPrice);
            Assert.AreEqual(8, matched2.Aggressor.Quantity);
            Assert.AreEqual(8, matched2.Aggressor.FilledQuantity);
            Assert.AreEqual(0, matched2.Aggressor.RemainingQuantity);
        }

        [Test]
        public void CreateLimitOrder_MatchBuyAgainstOrderAtSamePriceByTime()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now1 = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now1);
            var book = new OrderBook.OrderBook(sec, timeProvider);
            book.SetStatus(OrderBookStatus.Open);

            var id1 = Guid.NewGuid();
            book.CreateLimitOrder(id1, TimeInForce.Day, Side.Sell, 90, 5);
            var now2 = new DateTime(2000, 1, 1, 12, 1, 0);
            timeProvider.SetCurrentTime(now2);
            var id2 = Guid.NewGuid();
            book.CreateLimitOrder(id2, TimeInForce.Day, Side.Sell, 90, 5);
            var now3 = new DateTime(2000, 1, 1, 12, 2, 0);
            timeProvider.SetCurrentTime(now3);
            var id3 = Guid.NewGuid();

            // act
            var events = book.CreateLimitOrder(id3, TimeInForce.Day, Side.Buy, 100, 3).ToList();

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(2, events.Count);

            var matched = events[1] as OrderMatchedEvent;
            Assert.IsNotNull(matched);
            Assert.AreEqual(now3, matched.Fill.Time);
            Assert.AreEqual(90, matched.Fill.Price);
            Assert.AreEqual(3, matched.Fill.Quantity);

            Assert.AreEqual(id1, matched.Resting.Id);
            Assert.AreEqual(sec, matched.Resting.Security);
            Assert.AreEqual(now1, matched.Resting.CreatedTime);
            Assert.AreEqual(now1, matched.Resting.ModifiedTime);
            Assert.IsNull(matched.Resting.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched.Resting.Status);
            Assert.AreEqual(OrderType.Limit, matched.Resting.Type);
            Assert.AreEqual(TimeInForce.Day, matched.Resting.TimeInForce);
            Assert.AreEqual(Side.Sell, matched.Resting.Side);
            Assert.AreEqual(90, matched.Resting.Price);
            Assert.IsNull(matched.Resting.StopPrice);
            Assert.AreEqual(5, matched.Resting.Quantity);
            Assert.AreEqual(3, matched.Resting.FilledQuantity);
            Assert.AreEqual(2, matched.Resting.RemainingQuantity);

            Assert.AreEqual(id3, matched.Aggressor.Id);
            Assert.AreEqual(sec, matched.Aggressor.Security);
            Assert.AreEqual(now3, matched.Aggressor.CreatedTime);
            Assert.AreEqual(now3, matched.Aggressor.ModifiedTime);
            Assert.AreEqual(now3, matched.Aggressor.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched.Aggressor.Status);
            Assert.AreEqual(OrderType.Limit, matched.Aggressor.Type);
            Assert.AreEqual(TimeInForce.Day, matched.Aggressor.TimeInForce);
            Assert.AreEqual(Side.Buy, matched.Aggressor.Side);
            Assert.AreEqual(100, matched.Aggressor.Price);
            Assert.IsNull(matched.Aggressor.StopPrice);
            Assert.AreEqual(3, matched.Aggressor.Quantity);
            Assert.AreEqual(3, matched.Aggressor.FilledQuantity);
            Assert.AreEqual(0, matched.Aggressor.RemainingQuantity);
        }

        [Test]
        public void CreateLimitOrder_MatchBuyAgainstOrdersAtSamePriceByTime()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now1 = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now1);
            var book = new OrderBook.OrderBook(sec, timeProvider);
            book.SetStatus(OrderBookStatus.Open);

            var id1 = Guid.NewGuid();
            book.CreateLimitOrder(id1, TimeInForce.Day, Side.Sell, 90, 5);
            var now2 = new DateTime(2000, 1, 1, 12, 1, 0);
            timeProvider.SetCurrentTime(now2);
            var id2 = Guid.NewGuid();
            book.CreateLimitOrder(id2, TimeInForce.Day, Side.Sell, 90, 5);
            var now3 = new DateTime(2000, 1, 1, 12, 2, 0);
            timeProvider.SetCurrentTime(now3);
            var id3 = Guid.NewGuid();

            // act
            var events = book.CreateLimitOrder(id3, TimeInForce.Day, Side.Buy, 100, 8).ToList();

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(3, events.Count);

            var matched1 = events[1] as OrderMatchedEvent;
            Assert.IsNotNull(matched1);
            Assert.AreEqual(now3, matched1.Fill.Time);
            Assert.AreEqual(90, matched1.Fill.Price);
            Assert.AreEqual(5, matched1.Fill.Quantity);

            Assert.AreEqual(id1, matched1.Resting.Id);
            Assert.AreEqual(sec, matched1.Resting.Security);
            Assert.AreEqual(now1, matched1.Resting.CreatedTime);
            Assert.AreEqual(now1, matched1.Resting.ModifiedTime);
            Assert.AreEqual(now3, matched1.Resting.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched1.Resting.Status);
            Assert.AreEqual(OrderType.Limit, matched1.Resting.Type);
            Assert.AreEqual(TimeInForce.Day, matched1.Resting.TimeInForce);
            Assert.AreEqual(Side.Sell, matched1.Resting.Side);
            Assert.AreEqual(90, matched1.Resting.Price);
            Assert.IsNull(matched1.Resting.StopPrice);
            Assert.AreEqual(5, matched1.Resting.Quantity);
            Assert.AreEqual(5, matched1.Resting.FilledQuantity);
            Assert.AreEqual(0, matched1.Resting.RemainingQuantity);

            Assert.AreEqual(id3, matched1.Aggressor.Id);
            Assert.AreEqual(sec, matched1.Aggressor.Security);
            Assert.AreEqual(now3, matched1.Aggressor.CreatedTime);
            Assert.AreEqual(now3, matched1.Aggressor.ModifiedTime);
            Assert.IsNull(matched1.Aggressor.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched1.Aggressor.Status);
            Assert.AreEqual(OrderType.Limit, matched1.Aggressor.Type);
            Assert.AreEqual(TimeInForce.Day, matched1.Aggressor.TimeInForce);
            Assert.AreEqual(Side.Buy, matched1.Aggressor.Side);
            Assert.AreEqual(100, matched1.Aggressor.Price);
            Assert.IsNull(matched1.Aggressor.StopPrice);
            Assert.AreEqual(8, matched1.Aggressor.Quantity);
            Assert.AreEqual(5, matched1.Aggressor.FilledQuantity);
            Assert.AreEqual(3, matched1.Aggressor.RemainingQuantity);

            var matched2 = events[2] as OrderMatchedEvent;
            Assert.IsNotNull(matched2);
            Assert.AreEqual(now3, matched2.Fill.Time);
            Assert.AreEqual(90, matched2.Fill.Price);
            Assert.AreEqual(3, matched2.Fill.Quantity);

            Assert.AreEqual(id2, matched2.Resting.Id);
            Assert.AreEqual(sec, matched2.Resting.Security);
            Assert.AreEqual(now2, matched2.Resting.CreatedTime);
            Assert.AreEqual(now2, matched2.Resting.ModifiedTime);
            Assert.IsNull(matched2.Resting.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched2.Resting.Status);
            Assert.AreEqual(OrderType.Limit, matched2.Resting.Type);
            Assert.AreEqual(TimeInForce.Day, matched2.Resting.TimeInForce);
            Assert.AreEqual(Side.Sell, matched2.Resting.Side);
            Assert.AreEqual(90, matched2.Resting.Price);
            Assert.IsNull(matched2.Resting.StopPrice);
            Assert.AreEqual(5, matched2.Resting.Quantity);
            Assert.AreEqual(3, matched2.Resting.FilledQuantity);
            Assert.AreEqual(2, matched2.Resting.RemainingQuantity);

            Assert.AreEqual(id3, matched2.Aggressor.Id);
            Assert.AreEqual(sec, matched2.Aggressor.Security);
            Assert.AreEqual(now3, matched2.Aggressor.CreatedTime);
            Assert.AreEqual(now3, matched2.Aggressor.ModifiedTime);
            Assert.AreEqual(now3, matched2.Aggressor.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched2.Aggressor.Status);
            Assert.AreEqual(OrderType.Limit, matched2.Aggressor.Type);
            Assert.AreEqual(TimeInForce.Day, matched2.Aggressor.TimeInForce);
            Assert.AreEqual(Side.Buy, matched2.Aggressor.Side);
            Assert.AreEqual(100, matched2.Aggressor.Price);
            Assert.IsNull(matched2.Aggressor.StopPrice);
            Assert.AreEqual(8, matched2.Aggressor.Quantity);
            Assert.AreEqual(8, matched2.Aggressor.FilledQuantity);
            Assert.AreEqual(0, matched2.Aggressor.RemainingQuantity);
        }

        [Test]
        public void CreateLimitOrder_MatchAgainstOrderAtSamePriceByTimeAfterIncreaseQuantity()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now1 = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now1);
            var book = new OrderBook.OrderBook(sec, timeProvider);
            book.SetStatus(OrderBookStatus.Open);

            var id1 = Guid.NewGuid();
            book.CreateLimitOrder(id1, TimeInForce.Day, Side.Buy, 110, 5);
            var now2 = new DateTime(2000, 1, 1, 12, 1, 0);
            timeProvider.SetCurrentTime(now2);
            var id2 = Guid.NewGuid();
            book.CreateLimitOrder(id2, TimeInForce.Day, Side.Buy, 110, 5);
            var now3 = new DateTime(2000, 1, 1, 12, 2, 0);
            timeProvider.SetCurrentTime(now3);
            book.UpdateLimitOrder(id1, 110, 7);
            var now4 = new DateTime(2000, 1, 1, 12, 3, 0);
            timeProvider.SetCurrentTime(now4);
            var id3 = Guid.NewGuid();

            // act
            var events = book.CreateLimitOrder(id3, TimeInForce.Day, Side.Sell, 100, 3).ToList();

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(2, events.Count);

            var matched = events[1] as OrderMatchedEvent;
            Assert.IsNotNull(matched);
            Assert.AreEqual(now4, matched.Fill.Time);
            Assert.AreEqual(110, matched.Fill.Price);
            Assert.AreEqual(3, matched.Fill.Quantity);

            Assert.AreEqual(id2, matched.Resting.Id);
            Assert.AreEqual(sec, matched.Resting.Security);
            Assert.AreEqual(now2, matched.Resting.CreatedTime);
            Assert.AreEqual(now2, matched.Resting.ModifiedTime);
            Assert.IsNull(matched.Resting.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched.Resting.Status);
            Assert.AreEqual(OrderType.Limit, matched.Resting.Type);
            Assert.AreEqual(TimeInForce.Day, matched.Resting.TimeInForce);
            Assert.AreEqual(Side.Buy, matched.Resting.Side);
            Assert.AreEqual(110, matched.Resting.Price);
            Assert.IsNull(matched.Resting.StopPrice);
            Assert.AreEqual(5, matched.Resting.Quantity);
            Assert.AreEqual(3, matched.Resting.FilledQuantity);
            Assert.AreEqual(2, matched.Resting.RemainingQuantity);

            Assert.AreEqual(id3, matched.Aggressor.Id);
            Assert.AreEqual(sec, matched.Aggressor.Security);
            Assert.AreEqual(now4, matched.Aggressor.CreatedTime);
            Assert.AreEqual(now4, matched.Aggressor.ModifiedTime);
            Assert.AreEqual(now4, matched.Aggressor.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched.Aggressor.Status);
            Assert.AreEqual(OrderType.Limit, matched.Aggressor.Type);
            Assert.AreEqual(TimeInForce.Day, matched.Aggressor.TimeInForce);
            Assert.AreEqual(Side.Sell, matched.Aggressor.Side);
            Assert.AreEqual(100, matched.Aggressor.Price);
            Assert.IsNull(matched.Aggressor.StopPrice);
            Assert.AreEqual(3, matched.Aggressor.Quantity);
            Assert.AreEqual(3, matched.Aggressor.FilledQuantity);
            Assert.AreEqual(0, matched.Aggressor.RemainingQuantity);
        }

        [Test]
        public void CreateLimitOrder_MatchAgainstOrdersAtSamePriceByTimeAfterIncreaseQuantity()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now1 = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now1);
            var book = new OrderBook.OrderBook(sec, timeProvider);
            book.SetStatus(OrderBookStatus.Open);

            var id1 = Guid.NewGuid();
            book.CreateLimitOrder(id1, TimeInForce.Day, Side.Buy, 110, 5);
            var now2 = new DateTime(2000, 1, 1, 12, 1, 0);
            timeProvider.SetCurrentTime(now2);
            var id2 = Guid.NewGuid();
            book.CreateLimitOrder(id2, TimeInForce.Day, Side.Buy, 110, 5);
            var now3 = new DateTime(2000, 1, 1, 12, 2, 0);
            timeProvider.SetCurrentTime(now3);
            book.UpdateLimitOrder(id1, 110, 6);
            var now4 = new DateTime(2000, 1, 1, 12, 2, 0);
            timeProvider.SetCurrentTime(now4);
            var id3 = Guid.NewGuid();

            // act
            var events = book.CreateLimitOrder(id3, TimeInForce.Day, Side.Sell, 100, 8).ToList();

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(3, events.Count);

            var matched1 = events[1] as OrderMatchedEvent;
            Assert.IsNotNull(matched1);
            Assert.AreEqual(now4, matched1.Fill.Time);
            Assert.AreEqual(110, matched1.Fill.Price);
            Assert.AreEqual(5, matched1.Fill.Quantity);

            Assert.AreEqual(id2, matched1.Resting.Id);
            Assert.AreEqual(sec, matched1.Resting.Security);
            Assert.AreEqual(now2, matched1.Resting.CreatedTime);
            Assert.AreEqual(now2, matched1.Resting.ModifiedTime);
            Assert.AreEqual(now4, matched1.Resting.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched1.Resting.Status);
            Assert.AreEqual(OrderType.Limit, matched1.Resting.Type);
            Assert.AreEqual(TimeInForce.Day, matched1.Resting.TimeInForce);
            Assert.AreEqual(Side.Buy, matched1.Resting.Side);
            Assert.AreEqual(110, matched1.Resting.Price);
            Assert.IsNull(matched1.Resting.StopPrice);
            Assert.AreEqual(5, matched1.Resting.Quantity);
            Assert.AreEqual(5, matched1.Resting.FilledQuantity);
            Assert.AreEqual(0, matched1.Resting.RemainingQuantity);

            Assert.AreEqual(id3, matched1.Aggressor.Id);
            Assert.AreEqual(sec, matched1.Aggressor.Security);
            Assert.AreEqual(now4, matched1.Aggressor.CreatedTime);
            Assert.AreEqual(now4, matched1.Aggressor.ModifiedTime);
            Assert.IsNull(matched1.Aggressor.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched1.Aggressor.Status);
            Assert.AreEqual(OrderType.Limit, matched1.Aggressor.Type);
            Assert.AreEqual(TimeInForce.Day, matched1.Aggressor.TimeInForce);
            Assert.AreEqual(Side.Sell, matched1.Aggressor.Side);
            Assert.AreEqual(100, matched1.Aggressor.Price);
            Assert.IsNull(matched1.Aggressor.StopPrice);
            Assert.AreEqual(8, matched1.Aggressor.Quantity);
            Assert.AreEqual(5, matched1.Aggressor.FilledQuantity);
            Assert.AreEqual(3, matched1.Aggressor.RemainingQuantity);

            var matched2 = events[2] as OrderMatchedEvent;
            Assert.IsNotNull(matched2);
            Assert.AreEqual(now4, matched2.Fill.Time);
            Assert.AreEqual(110, matched2.Fill.Price);
            Assert.AreEqual(3, matched2.Fill.Quantity);

            Assert.AreEqual(id1, matched2.Resting.Id);
            Assert.AreEqual(sec, matched2.Resting.Security);
            Assert.AreEqual(now1, matched2.Resting.CreatedTime);
            Assert.AreEqual(now3, matched2.Resting.ModifiedTime);
            Assert.IsNull(matched2.Resting.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched2.Resting.Status);
            Assert.AreEqual(OrderType.Limit, matched2.Resting.Type);
            Assert.AreEqual(TimeInForce.Day, matched2.Resting.TimeInForce);
            Assert.AreEqual(Side.Buy, matched2.Resting.Side);
            Assert.AreEqual(110, matched2.Resting.Price);
            Assert.IsNull(matched2.Resting.StopPrice);
            Assert.AreEqual(6, matched2.Resting.Quantity);
            Assert.AreEqual(3, matched2.Resting.FilledQuantity);
            Assert.AreEqual(3, matched2.Resting.RemainingQuantity);

            Assert.AreEqual(id3, matched2.Aggressor.Id);
            Assert.AreEqual(sec, matched2.Aggressor.Security);
            Assert.AreEqual(now4, matched2.Aggressor.CreatedTime);
            Assert.AreEqual(now4, matched2.Aggressor.ModifiedTime);
            Assert.AreEqual(now4, matched2.Aggressor.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched2.Aggressor.Status);
            Assert.AreEqual(OrderType.Limit, matched2.Aggressor.Type);
            Assert.AreEqual(TimeInForce.Day, matched2.Aggressor.TimeInForce);
            Assert.AreEqual(Side.Sell, matched2.Aggressor.Side);
            Assert.AreEqual(100, matched2.Aggressor.Price);
            Assert.IsNull(matched2.Aggressor.StopPrice);
            Assert.AreEqual(8, matched2.Aggressor.Quantity);
            Assert.AreEqual(8, matched2.Aggressor.FilledQuantity);
            Assert.AreEqual(0, matched2.Aggressor.RemainingQuantity);
        }

        [Test]
        public void CreateLimitOrder_MatchAgainstOrderAtSamePriceByTimeAfterDecreaseQuantity()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now1 = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now1);
            var book = new OrderBook.OrderBook(sec, timeProvider);
            book.SetStatus(OrderBookStatus.Open);

            var id1 = Guid.NewGuid();
            book.CreateLimitOrder(id1, TimeInForce.Day, Side.Buy, 110, 5);
            var now2 = new DateTime(2000, 1, 1, 12, 1, 0);
            timeProvider.SetCurrentTime(now2);
            var id2 = Guid.NewGuid();
            book.CreateLimitOrder(id2, TimeInForce.Day, Side.Buy, 110, 5);
            var now3 = new DateTime(2000, 1, 1, 12, 2, 0);
            timeProvider.SetCurrentTime(now3);
            book.UpdateLimitOrder(id1, 110, 4);
            var now4 = new DateTime(2000, 1, 1, 12, 3, 0);
            timeProvider.SetCurrentTime(now4);
            var id3 = Guid.NewGuid();

            // act
            var events = book.CreateLimitOrder(id3, TimeInForce.Day, Side.Sell, 100, 3).ToList();

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(2, events.Count);

            var matched = events[1] as OrderMatchedEvent;
            Assert.IsNotNull(matched);
            Assert.AreEqual(now4, matched.Fill.Time);
            Assert.AreEqual(110, matched.Fill.Price);
            Assert.AreEqual(3, matched.Fill.Quantity);

            Assert.AreEqual(id1, matched.Resting.Id);
            Assert.AreEqual(sec, matched.Resting.Security);
            Assert.AreEqual(now1, matched.Resting.CreatedTime);
            Assert.AreEqual(now3, matched.Resting.ModifiedTime);
            Assert.IsNull(matched.Resting.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched.Resting.Status);
            Assert.AreEqual(OrderType.Limit, matched.Resting.Type);
            Assert.AreEqual(TimeInForce.Day, matched.Resting.TimeInForce);
            Assert.AreEqual(Side.Buy, matched.Resting.Side);
            Assert.AreEqual(110, matched.Resting.Price);
            Assert.IsNull(matched.Resting.StopPrice);
            Assert.AreEqual(4, matched.Resting.Quantity);
            Assert.AreEqual(3, matched.Resting.FilledQuantity);
            Assert.AreEqual(1, matched.Resting.RemainingQuantity);

            Assert.AreEqual(id3, matched.Aggressor.Id);
            Assert.AreEqual(sec, matched.Aggressor.Security);
            Assert.AreEqual(now4, matched.Aggressor.CreatedTime);
            Assert.AreEqual(now4, matched.Aggressor.ModifiedTime);
            Assert.AreEqual(now4, matched.Aggressor.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched.Aggressor.Status);
            Assert.AreEqual(OrderType.Limit, matched.Aggressor.Type);
            Assert.AreEqual(TimeInForce.Day, matched.Aggressor.TimeInForce);
            Assert.AreEqual(Side.Sell, matched.Aggressor.Side);
            Assert.AreEqual(100, matched.Aggressor.Price);
            Assert.IsNull(matched.Aggressor.StopPrice);
            Assert.AreEqual(3, matched.Aggressor.Quantity);
            Assert.AreEqual(3, matched.Aggressor.FilledQuantity);
            Assert.AreEqual(0, matched.Aggressor.RemainingQuantity);
        }

        [Test]
        public void CreateLimitOrder_MatchAgainstOrdersAtSamePriceByTimeAfterDecreaseQuantity()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now1 = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now1);
            var book = new OrderBook.OrderBook(sec, timeProvider);
            book.SetStatus(OrderBookStatus.Open);

            var id1 = Guid.NewGuid();
            book.CreateLimitOrder(id1, TimeInForce.Day, Side.Buy, 110, 5);
            var now2 = new DateTime(2000, 1, 1, 12, 1, 0);
            timeProvider.SetCurrentTime(now2);
            var id2 = Guid.NewGuid();
            book.CreateLimitOrder(id2, TimeInForce.Day, Side.Buy, 110, 5);
            var now3 = new DateTime(2000, 1, 1, 12, 2, 0);
            timeProvider.SetCurrentTime(now3);
            book.UpdateLimitOrder(id1, 110, 4);
            var now4 = new DateTime(2000, 1, 1, 12, 3, 0);
            timeProvider.SetCurrentTime(now4);
            var id3 = Guid.NewGuid();

            // act
            var events = book.CreateLimitOrder(id3, TimeInForce.Day, Side.Sell, 100, 8).ToList();

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(3, events.Count);

            var matched1 = events[1] as OrderMatchedEvent;
            Assert.IsNotNull(matched1);
            Assert.AreEqual(now4, matched1.Fill.Time);
            Assert.AreEqual(110, matched1.Fill.Price);
            Assert.AreEqual(4, matched1.Fill.Quantity);

            Assert.AreEqual(id1, matched1.Resting.Id);
            Assert.AreEqual(sec, matched1.Resting.Security);
            Assert.AreEqual(now1, matched1.Resting.CreatedTime);
            Assert.AreEqual(now3, matched1.Resting.ModifiedTime);
            Assert.AreEqual(now4, matched1.Resting.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched1.Resting.Status);
            Assert.AreEqual(OrderType.Limit, matched1.Resting.Type);
            Assert.AreEqual(TimeInForce.Day, matched1.Resting.TimeInForce);
            Assert.AreEqual(Side.Buy, matched1.Resting.Side);
            Assert.AreEqual(110, matched1.Resting.Price);
            Assert.IsNull(matched1.Resting.StopPrice);
            Assert.AreEqual(4, matched1.Resting.Quantity);
            Assert.AreEqual(4, matched1.Resting.FilledQuantity);
            Assert.AreEqual(0, matched1.Resting.RemainingQuantity);

            Assert.AreEqual(id3, matched1.Aggressor.Id);
            Assert.AreEqual(sec, matched1.Aggressor.Security);
            Assert.AreEqual(now4, matched1.Aggressor.CreatedTime);
            Assert.AreEqual(now4, matched1.Aggressor.ModifiedTime);
            Assert.IsNull(matched1.Aggressor.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched1.Aggressor.Status);
            Assert.AreEqual(OrderType.Limit, matched1.Aggressor.Type);
            Assert.AreEqual(TimeInForce.Day, matched1.Aggressor.TimeInForce);
            Assert.AreEqual(Side.Sell, matched1.Aggressor.Side);
            Assert.AreEqual(100, matched1.Aggressor.Price);
            Assert.IsNull(matched1.Aggressor.StopPrice);
            Assert.AreEqual(8, matched1.Aggressor.Quantity);
            Assert.AreEqual(4, matched1.Aggressor.FilledQuantity);
            Assert.AreEqual(4, matched1.Aggressor.RemainingQuantity);

            var matched2 = events[2] as OrderMatchedEvent;
            Assert.IsNotNull(matched2);
            Assert.AreEqual(now4, matched2.Fill.Time);
            Assert.AreEqual(110, matched2.Fill.Price);
            Assert.AreEqual(4, matched2.Fill.Quantity);

            Assert.AreEqual(id2, matched2.Resting.Id);
            Assert.AreEqual(sec, matched2.Resting.Security);
            Assert.AreEqual(now2, matched2.Resting.CreatedTime);
            Assert.AreEqual(now2, matched2.Resting.ModifiedTime);
            Assert.IsNull(matched2.Resting.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched2.Resting.Status);
            Assert.AreEqual(OrderType.Limit, matched2.Resting.Type);
            Assert.AreEqual(TimeInForce.Day, matched2.Resting.TimeInForce);
            Assert.AreEqual(Side.Buy, matched2.Resting.Side);
            Assert.AreEqual(110, matched2.Resting.Price);
            Assert.IsNull(matched2.Resting.StopPrice);
            Assert.AreEqual(5, matched2.Resting.Quantity);
            Assert.AreEqual(4, matched2.Resting.FilledQuantity);
            Assert.AreEqual(1, matched2.Resting.RemainingQuantity);

            Assert.AreEqual(id3, matched2.Aggressor.Id);
            Assert.AreEqual(sec, matched2.Aggressor.Security);
            Assert.AreEqual(now4, matched2.Aggressor.CreatedTime);
            Assert.AreEqual(now4, matched2.Aggressor.ModifiedTime);
            Assert.AreEqual(now4, matched2.Aggressor.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched2.Aggressor.Status);
            Assert.AreEqual(OrderType.Limit, matched2.Aggressor.Type);
            Assert.AreEqual(TimeInForce.Day, matched2.Aggressor.TimeInForce);
            Assert.AreEqual(Side.Sell, matched2.Aggressor.Side);
            Assert.AreEqual(100, matched2.Aggressor.Price);
            Assert.IsNull(matched2.Aggressor.StopPrice);
            Assert.AreEqual(8, matched2.Aggressor.Quantity);
            Assert.AreEqual(8, matched2.Aggressor.FilledQuantity);
            Assert.AreEqual(0, matched2.Aggressor.RemainingQuantity);
        }

        [Test]
        public void CreateLimitOrder_MarketClosed_Rejected()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now);
            var book = new OrderBook.OrderBook(sec, timeProvider);
            var id = Guid.NewGuid();

            // act
            var events = book.CreateLimitOrder(id, TimeInForce.Day, Side.Buy, 100, 3).ToList();

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as OrderCreateRejectedEvent;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(OrderRejectedReason.MarketClosed, rejected.Reason);
            Assert.AreEqual(id, rejected.OrderId);
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void CreateLimitOrder_InvalidQuantity_Rejected(int quantity)
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now);
            var book = new OrderBook.OrderBook(sec, timeProvider);

            book.SetStatus(OrderBookStatus.Open);
            var id = Guid.NewGuid();

            // act
            var events = book.CreateLimitOrder(id, TimeInForce.Day, Side.Buy, 100, quantity).ToList();

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as OrderCreateRejectedEvent;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(OrderRejectedReason.InvalidQuantity, rejected.Reason);
            Assert.AreEqual(id, rejected.OrderId);
        }

        [TestCase(8)]
        [TestCase(-8)]
        [TestCase(-108)]
        [TestCase(10.01)]
        public void CreateLimitOrder_InvalidPriceIncrement_Rejected(decimal price)
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now);
            var book = new OrderBook.OrderBook(sec, timeProvider);

            book.SetStatus(OrderBookStatus.Open);
            var id = Guid.NewGuid();

            // act
            var events = book.CreateLimitOrder(id, TimeInForce.Day, Side.Buy, price, 6).ToList();

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as OrderCreateRejectedEvent;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(OrderRejectedReason.InvalidPriceIncrement, rejected.Reason);
            Assert.AreEqual(id, rejected.OrderId);
        }

        [Test]
        public void UpdateLimitOrder_IncreaseQuantity_Success()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now1 = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now1);
            var book = new OrderBook.OrderBook(sec, timeProvider);

            book.SetStatus(OrderBookStatus.Open);
            var id = Guid.NewGuid();
            book.CreateLimitOrder(id, TimeInForce.Day, Side.Buy, 100, 3);
            var now2 = new DateTime(2000, 1, 1, 12, 1, 0);
            timeProvider.SetCurrentTime(now2);

            // act
            var events = book.UpdateLimitOrder(id, 110, 5).ToList();

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var updated = events[0] as OrderUpdatedEvent;
            Assert.IsNotNull(updated);
            Assert.AreEqual(id, updated.Order.Id);
            Assert.AreEqual(sec, updated.Order.Security);
            Assert.AreEqual(now1, updated.Order.CreatedTime);
            Assert.AreEqual(now2, updated.Order.ModifiedTime);
            Assert.IsNull(updated.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, updated.Order.Status);
            Assert.AreEqual(OrderType.Limit, updated.Order.Type);
            Assert.AreEqual(TimeInForce.Day, updated.Order.TimeInForce);
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
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now1 = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now1);
            var book = new OrderBook.OrderBook(sec, timeProvider);

            book.SetStatus(OrderBookStatus.Open);
            var id = Guid.NewGuid();
            book.CreateLimitOrder(id, TimeInForce.Day, Side.Buy, 100, 3);
            var now2 = new DateTime(2000, 1, 1, 12, 1, 0);
            timeProvider.SetCurrentTime(now2);

            // act
            var events = book.UpdateLimitOrder(id, 110, 1).ToList();

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var updated = events[0] as OrderUpdatedEvent;
            Assert.IsNotNull(updated);
            Assert.AreEqual(id, updated.Order.Id);
            Assert.AreEqual(sec, updated.Order.Security);
            Assert.AreEqual(now1, updated.Order.CreatedTime);
            Assert.AreEqual(now2, updated.Order.ModifiedTime);
            Assert.IsNull(updated.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, updated.Order.Status);
            Assert.AreEqual(OrderType.Limit, updated.Order.Type);
            Assert.AreEqual(TimeInForce.Day, updated.Order.TimeInForce);
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
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now1 = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now1);
            var book = new OrderBook.OrderBook(sec, timeProvider);

            book.SetStatus(OrderBookStatus.Open);
            var id = Guid.NewGuid();
            book.CreateLimitOrder(id, TimeInForce.Day, Side.Buy, 100, 5);
            book.CreateLimitOrder(Guid.NewGuid(), TimeInForce.Day, Side.Sell, 100, 4);
            var now2 = new DateTime(2000, 1, 1, 12, 1, 0);
            timeProvider.SetCurrentTime(now2);

            // act
            var events = book.UpdateLimitOrder(id, 100, 2).ToList();

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var cancelled = events[0] as OrderCancelledEvent;
            Assert.IsNotNull(cancelled);
            Assert.AreEqual(OrderCancelledReason.UpdatedQuantityLowerThanFilledQuantity, cancelled.Reason);
            Assert.AreEqual(id, cancelled.Order.Id);
            Assert.AreEqual(sec, cancelled.Order.Security);
            Assert.AreEqual(now1, cancelled.Order.CreatedTime);
            Assert.AreEqual(now1, cancelled.Order.ModifiedTime);
            Assert.AreEqual(now2, cancelled.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Cancelled, cancelled.Order.Status);
            Assert.AreEqual(OrderType.Limit, cancelled.Order.Type);
            Assert.AreEqual(TimeInForce.Day, cancelled.Order.TimeInForce);
            Assert.AreEqual(Side.Buy, cancelled.Order.Side);
            Assert.AreEqual(100, cancelled.Order.Price);
            Assert.IsNull(cancelled.Order.StopPrice);
            Assert.AreEqual(5, cancelled.Order.Quantity);
            Assert.AreEqual(4, cancelled.Order.FilledQuantity);
            Assert.AreEqual(0, cancelled.Order.RemainingQuantity);
        }

        [Test]
        public void UpdateLimitOrder_OrderFilled_Rejected()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now1 = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now1);
            var book = new OrderBook.OrderBook(sec, timeProvider);

            book.SetStatus(OrderBookStatus.Open);
            var id = Guid.NewGuid();
            book.CreateLimitOrder(id, TimeInForce.Day, Side.Buy, 100, 3);
            book.CreateLimitOrder(Guid.NewGuid(), TimeInForce.Day, Side.Sell, 100, 3);
            var now2 = new DateTime(2000, 1, 1, 12, 1, 0);
            timeProvider.SetCurrentTime(now2);

            // act
            var events = book.UpdateLimitOrder(id, 110, 5).ToList();

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as OrderUpdateRejectedEvent;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(id, rejected.OrderId);
            Assert.AreEqual(OrderRejectedReason.TooLateToCancel, rejected.Reason);
        }

        [Test]
        public void UpdateLimitOrder_OrderCancelled_Rejected()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now1 = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now1);
            var book = new OrderBook.OrderBook(sec, timeProvider);

            book.SetStatus(OrderBookStatus.Open);
            var id = Guid.NewGuid();
            book.CreateLimitOrder(id, TimeInForce.Day, Side.Buy, 100, 3);
            book.CancelOrder(id);
            var now2 = new DateTime(2000, 1, 1, 12, 1, 0);
            timeProvider.SetCurrentTime(now2);

            // act
            var events = book.UpdateLimitOrder(id, 110, 5).ToList();

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as OrderUpdateRejectedEvent;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(id, rejected.OrderId);
            Assert.AreEqual(OrderRejectedReason.TooLateToCancel, rejected.Reason);
        }

        [Test]
        public void UpdateLimitOrder_OrderExpired_Rejected()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now1 = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now1);
            var book = new OrderBook.OrderBook(sec, timeProvider);

            book.SetStatus(OrderBookStatus.Open);
            var id = Guid.NewGuid();
            book.CreateLimitOrder(id, TimeInForce.Day, Side.Buy, 100, 3);
            book.SetStatus(OrderBookStatus.Closed);
            book.SetStatus(OrderBookStatus.Open);

            // act
            var events = book.UpdateLimitOrder(id, 110, 5).ToList();

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as OrderUpdateRejectedEvent;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(id, rejected.OrderId);
            Assert.AreEqual(OrderRejectedReason.TooLateToCancel, rejected.Reason);
        }


        [Test]
        public void UpdateLimitOrder_NotFound_Rejected()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now);
            var book = new OrderBook.OrderBook(sec, timeProvider);

            book.SetStatus(OrderBookStatus.Open);
            var id = Guid.NewGuid();

            // act
            var events = book.UpdateLimitOrder(id, 110, 5).ToList();

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as OrderUpdateRejectedEvent;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(id, rejected.OrderId);
            Assert.AreEqual(OrderRejectedReason.OrderNotInBook, rejected.Reason);
        }

        [Test]
        public void UpdateLimitOrder_MarketClosed_Rejected()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now);
            var book = new OrderBook.OrderBook(sec, timeProvider);

            book.SetStatus(OrderBookStatus.Open);
            var id = Guid.NewGuid();
            book.CreateLimitOrder(id, TimeInForce.Day, Side.Buy, 100, 3);
            book.SetStatus(OrderBookStatus.Closed);

            // act
            var events = book.UpdateLimitOrder(id, 105, 5).ToList();

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as OrderUpdateRejectedEvent;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(OrderRejectedReason.MarketClosed, rejected.Reason);
            Assert.AreEqual(id, rejected.OrderId);
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void UpdateLimitOrder_InvalidQuantity_Rejected(int quantity)
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now);
            var book = new OrderBook.OrderBook(sec, timeProvider);

            book.SetStatus(OrderBookStatus.Open);
            var id = Guid.NewGuid();
            book.CreateLimitOrder(id, TimeInForce.Day, Side.Buy, 100, 3);

            // act
            var events = book.UpdateLimitOrder(id, 110, quantity).ToList();

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as OrderUpdateRejectedEvent;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(OrderRejectedReason.InvalidQuantity, rejected.Reason);
            Assert.AreEqual(id, rejected.OrderId);
        }

        [TestCase(8)]
        [TestCase(-8)]
        [TestCase(-108)]
        [TestCase(10.01)]
        public void UpdateLimitOrder_InvalidPriceIncrement_Rejected(decimal price)
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now);
            var book = new OrderBook.OrderBook(sec, timeProvider);

            book.SetStatus(OrderBookStatus.Open);
            var id = Guid.NewGuid();
            book.CreateLimitOrder(id, TimeInForce.Day, Side.Buy, 100, 6);

            // act
            var events = book.UpdateLimitOrder(id, price, 6).ToList();

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as OrderUpdateRejectedEvent;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(OrderRejectedReason.InvalidPriceIncrement, rejected.Reason);
            Assert.AreEqual(id, rejected.OrderId);
        }

        [Test]
        public void CancelLimitOrder_Valid_Success()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now1 = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now1);
            var book = new OrderBook.OrderBook(sec, timeProvider);

            book.SetStatus(OrderBookStatus.Open);
            var id = Guid.NewGuid();
            book.CreateLimitOrder(id, TimeInForce.Day, Side.Buy, 100, 3);
            var now2 = new DateTime(2000, 1, 1, 12, 1, 0);
            timeProvider.SetCurrentTime(now2);

            // act
            var events = book.CancelOrder(id).ToList();

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var cancelled = events[0] as OrderCancelledEvent;
            Assert.IsNotNull(cancelled);
            Assert.AreEqual(OrderCancelledReason.Cancelled, cancelled.Reason);
            Assert.AreEqual(id, cancelled.Order.Id);
            Assert.AreEqual(sec, cancelled.Order.Security);
            Assert.AreEqual(now1, cancelled.Order.CreatedTime);
            Assert.AreEqual(now1, cancelled.Order.ModifiedTime);
            Assert.AreEqual(now2, cancelled.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Cancelled, cancelled.Order.Status);
            Assert.AreEqual(OrderType.Limit, cancelled.Order.Type);
            Assert.AreEqual(TimeInForce.Day, cancelled.Order.TimeInForce);
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
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now);
            var book = new OrderBook.OrderBook(sec, timeProvider);

            book.SetStatus(OrderBookStatus.Open);
            var id = Guid.NewGuid();

            // act
            var events = book.CancelOrder(id).ToList();

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as OrderCancelRejectedEvent;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(id, rejected.OrderId);
            Assert.AreEqual(OrderRejectedReason.OrderNotInBook, rejected.Reason);
        }

        [Test]
        public void CancelLimitOrder_MarketClosed_Rejected()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now);
            var book = new OrderBook.OrderBook(sec, timeProvider);

            book.SetStatus(OrderBookStatus.Open);
            var id = Guid.NewGuid();
            book.CreateLimitOrder(id, TimeInForce.Day, Side.Buy, 100, 3);
            book.SetStatus(OrderBookStatus.Closed);

            // act
            var events = book.CancelOrder(id).ToList();

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as OrderCancelRejectedEvent;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(OrderRejectedReason.MarketClosed, rejected.Reason);
            Assert.AreEqual(id, rejected.OrderId);
        }

        [Test]
        public void CreateMarketOrder_Valid_Success()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10, 20);
            var now1 = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now1);
            var book = new OrderBook.OrderBook(sec, timeProvider);
            book.SetStatus(OrderBookStatus.Open);

            var id1 = Guid.NewGuid();
            book.CreateLimitOrder(id1, TimeInForce.Day, Side.Buy, 500, 3);
            var now2 = new DateTime(2000, 1, 1, 12, 1, 0);
            timeProvider.SetCurrentTime(now2);
            var id2 = Guid.NewGuid();

            // act
            var events = book.CreateMarketOrder(id2, TimeInForce.Day, Side.Sell, 5).ToList();

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(2, events.Count);

            var matched = events[1] as OrderMatchedEvent;
            Assert.IsNotNull(matched);
            Assert.AreEqual(now2, matched.Fill.Time);
            Assert.AreEqual(500, matched.Fill.Price);
            Assert.AreEqual(3, matched.Fill.Quantity);

            Assert.AreEqual(id1, matched.Resting.Id);
            Assert.AreEqual(sec, matched.Resting.Security);
            Assert.AreEqual(now1, matched.Resting.CreatedTime);
            Assert.AreEqual(now1, matched.Resting.ModifiedTime);
            Assert.AreEqual(now2, matched.Resting.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, matched.Resting.Status);
            Assert.AreEqual(OrderType.Limit, matched.Resting.Type);
            Assert.AreEqual(TimeInForce.Day, matched.Resting.TimeInForce);
            Assert.AreEqual(Side.Buy, matched.Resting.Side);
            Assert.AreEqual(500, matched.Resting.Price);
            Assert.IsNull(matched.Resting.StopPrice);
            Assert.AreEqual(3, matched.Resting.Quantity);
            Assert.AreEqual(3, matched.Resting.FilledQuantity);
            Assert.AreEqual(0, matched.Resting.RemainingQuantity);

            Assert.AreEqual(id2, matched.Aggressor.Id);
            Assert.AreEqual(sec, matched.Aggressor.Security);
            Assert.AreEqual(now2, matched.Aggressor.CreatedTime);
            Assert.AreEqual(now2, matched.Aggressor.ModifiedTime);
            Assert.IsNull(matched.Aggressor.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, matched.Aggressor.Status);
            Assert.AreEqual(OrderType.Limit, matched.Aggressor.Type);
            Assert.AreEqual(TimeInForce.Day, matched.Aggressor.TimeInForce);
            Assert.AreEqual(Side.Sell, matched.Aggressor.Side);
            Assert.AreEqual(300, matched.Aggressor.Price);
            Assert.IsNull(matched.Aggressor.StopPrice);
            Assert.AreEqual(5, matched.Aggressor.Quantity);
            Assert.AreEqual(3, matched.Aggressor.FilledQuantity);
            Assert.AreEqual(2, matched.Aggressor.RemainingQuantity);
        }

        [Test]
        public void CreateMarketOrder_MarketClosed_Rejected()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now);
            var book = new OrderBook.OrderBook(sec, timeProvider);

            var id = Guid.NewGuid();

            // act
            var events = book.CreateMarketOrder(id, TimeInForce.Day, Side.Buy, 3).ToList();

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as OrderCreateRejectedEvent;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(OrderRejectedReason.MarketClosed, rejected.Reason);
            Assert.AreEqual(id, rejected.OrderId);
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void CreateMarketOrder_InvalidQuantity_Rejected(int quantity)
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now);
            var book = new OrderBook.OrderBook(sec, timeProvider);

            book.SetStatus(OrderBookStatus.Open);
            var id = Guid.NewGuid();

            // act
            var events = book.CreateMarketOrder(id, TimeInForce.Day, Side.Buy, quantity).ToList();

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as OrderCreateRejectedEvent;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(OrderRejectedReason.InvalidQuantity, rejected.Reason);
            Assert.AreEqual(id, rejected.OrderId);
        }

        [Test]
        public void CreateMarketOrder_EmptyBook_Rejected()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now);
            var book = new OrderBook.OrderBook(sec, timeProvider);

            book.SetStatus(OrderBookStatus.Open);
            var id = Guid.NewGuid();

            // act
            var events = book.CreateMarketOrder(id, TimeInForce.Day, Side.Buy, 3).ToList();

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as OrderCreateRejectedEvent;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(OrderRejectedReason.NoOrdersToMatchMarketOrder, rejected.Reason);
            Assert.AreEqual(id, rejected.OrderId);
        }

        [Test]
        public void SetStatusClosed_ExpireDayOrders()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now1 = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now1);
            var book = new OrderBook.OrderBook(sec, timeProvider);

            book.SetStatus(OrderBookStatus.Open);
            var id = Guid.NewGuid();
            book.CreateLimitOrder(id, TimeInForce.Day, Side.Buy, 100, 5);
            var now2 = new DateTime(2000, 1, 1, 12, 1, 0);
            timeProvider.SetCurrentTime(now2);

            // act
            var events = book.SetStatus(OrderBookStatus.Closed).ToList();

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var expired = events[0] as OrderExpiredEvent;
            Assert.IsNotNull(expired);
            Assert.AreEqual(id, expired.Order.Id);
            Assert.AreEqual(sec, expired.Order.Security);
            Assert.AreEqual(now1, expired.Order.CreatedTime);
            Assert.AreEqual(now1, expired.Order.ModifiedTime);
            Assert.AreEqual(now2, expired.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Expired, expired.Order.Status);
            Assert.AreEqual(OrderType.Limit, expired.Order.Type);
            Assert.AreEqual(TimeInForce.Day, expired.Order.TimeInForce);
            Assert.AreEqual(Side.Buy, expired.Order.Side);
            Assert.AreEqual(100, expired.Order.Price);
            Assert.IsNull(expired.Order.StopPrice);
            Assert.AreEqual(5, expired.Order.Quantity);
            Assert.AreEqual(0, expired.Order.FilledQuantity);
            Assert.AreEqual(0, expired.Order.RemainingQuantity);
        }

        [Test]
        public void SetStatusClosed_AlreadyClosed_Exception()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now1 = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now1);
            var book = new OrderBook.OrderBook(sec, timeProvider);

            // act & assert
            Assert.Throws<Exception>(() => book.SetStatus(OrderBookStatus.Closed));
        }

        [Test]
        public void SetStatusClosed_AlreadyOpen_Exception()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now1 = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now1);
            var book = new OrderBook.OrderBook(sec, timeProvider);
            book.SetStatus(OrderBookStatus.Open);

            // act & assert
            Assert.Throws<Exception>(() => book.SetStatus(OrderBookStatus.Open));
        }
    }
}