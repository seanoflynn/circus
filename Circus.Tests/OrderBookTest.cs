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
            Assert.AreEqual(3, events.Count);

            var filled1 = events[1] as OrderFilledEvent;
            Assert.IsNotNull(filled1);
            Assert.AreEqual(id1, filled1.Fill.Order.Id);
            Assert.AreEqual(sec, filled1.Fill.Order.Security);
            Assert.AreEqual(now1, filled1.Fill.Order.CreatedTime);
            Assert.AreEqual(now1, filled1.Fill.Order.ModifiedTime);
            Assert.AreEqual(now2, filled1.Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, filled1.Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filled1.Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filled1.Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Buy, filled1.Fill.Order.Side);
            Assert.AreEqual(100, filled1.Fill.Order.Price);
            Assert.IsNull(filled1.Fill.Order.StopPrice);
            Assert.AreEqual(3, filled1.Fill.Order.Quantity);
            Assert.AreEqual(3, filled1.Fill.Order.FilledQuantity);
            Assert.AreEqual(0, filled1.Fill.Order.RemainingQuantity);
            Assert.AreEqual(now2, filled1.Fill.Time);
            Assert.AreEqual(100, filled1.Fill.Price);
            Assert.AreEqual(3, filled1.Fill.Quantity);
            Assert.IsFalse(filled1.Fill.IsAggressor);

            var filled2 = events[2] as OrderFilledEvent;
            Assert.IsNotNull(filled2);
            Assert.AreEqual(id2, filled2.Fill.Order.Id);
            Assert.AreEqual(sec, filled2.Fill.Order.Security);
            Assert.AreEqual(now2, filled2.Fill.Order.CreatedTime);
            Assert.AreEqual(now2, filled2.Fill.Order.ModifiedTime);
            Assert.IsNull(filled2.Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, filled2.Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filled2.Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filled2.Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Sell, filled2.Fill.Order.Side);
            Assert.AreEqual(100, filled2.Fill.Order.Price);
            Assert.IsNull(filled2.Fill.Order.StopPrice);
            Assert.AreEqual(5, filled2.Fill.Order.Quantity);
            Assert.AreEqual(3, filled2.Fill.Order.FilledQuantity);
            Assert.AreEqual(2, filled2.Fill.Order.RemainingQuantity);
            Assert.AreEqual(now2, filled2.Fill.Time);
            Assert.AreEqual(100, filled2.Fill.Price);
            Assert.AreEqual(3, filled2.Fill.Quantity);
            Assert.IsTrue(filled2.Fill.IsAggressor);
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
            Assert.AreEqual(3, events.Count);

            var filled1 = events[1] as OrderFilledEvent;
            Assert.IsNotNull(filled1);
            Assert.AreEqual(id1, filled1.Fill.Order.Id);
            Assert.AreEqual(sec, filled1.Fill.Order.Security);
            Assert.AreEqual(now1, filled1.Fill.Order.CreatedTime);
            Assert.AreEqual(now1, filled1.Fill.Order.ModifiedTime);
            Assert.AreEqual(now2, filled1.Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, filled1.Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filled1.Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filled1.Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Buy, filled1.Fill.Order.Side);
            Assert.AreEqual(110, filled1.Fill.Order.Price);
            Assert.IsNull(filled1.Fill.Order.StopPrice);
            Assert.AreEqual(3, filled1.Fill.Order.Quantity);
            Assert.AreEqual(3, filled1.Fill.Order.FilledQuantity);
            Assert.AreEqual(0, filled1.Fill.Order.RemainingQuantity);
            Assert.AreEqual(now2, filled1.Fill.Time);
            Assert.AreEqual(110, filled1.Fill.Price);
            Assert.AreEqual(3, filled1.Fill.Quantity);
            Assert.IsFalse(filled1.Fill.IsAggressor);

            var filled2 = events[2] as OrderFilledEvent;
            Assert.IsNotNull(filled2);
            Assert.AreEqual(id2, filled2.Fill.Order.Id);
            Assert.AreEqual(sec, filled2.Fill.Order.Security);
            Assert.AreEqual(now2, filled2.Fill.Order.CreatedTime);
            Assert.AreEqual(now2, filled2.Fill.Order.ModifiedTime);
            Assert.IsNull(filled2.Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, filled2.Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filled2.Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filled2.Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Sell, filled2.Fill.Order.Side);
            Assert.AreEqual(100, filled2.Fill.Order.Price);
            Assert.IsNull(filled2.Fill.Order.StopPrice);
            Assert.AreEqual(5, filled2.Fill.Order.Quantity);
            Assert.AreEqual(3, filled2.Fill.Order.FilledQuantity);
            Assert.AreEqual(2, filled2.Fill.Order.RemainingQuantity);
            Assert.AreEqual(now2, filled2.Fill.Time);
            Assert.AreEqual(110, filled2.Fill.Price);
            Assert.AreEqual(3, filled2.Fill.Quantity);
            Assert.IsTrue(filled2.Fill.IsAggressor);
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
            Assert.AreEqual(3, events.Count);

            var filled1 = events[1] as OrderFilledEvent;
            Assert.IsNotNull(filled1);
            Assert.AreEqual(id1, filled1.Fill.Order.Id);
            Assert.AreEqual(sec, filled1.Fill.Order.Security);
            Assert.AreEqual(now1, filled1.Fill.Order.CreatedTime);
            Assert.AreEqual(now1, filled1.Fill.Order.ModifiedTime);
            Assert.IsNull(filled1.Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, filled1.Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filled1.Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filled1.Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Buy, filled1.Fill.Order.Side);
            Assert.AreEqual(110, filled1.Fill.Order.Price);
            Assert.IsNull(filled1.Fill.Order.StopPrice);
            Assert.AreEqual(5, filled1.Fill.Order.Quantity);
            Assert.AreEqual(3, filled1.Fill.Order.FilledQuantity);
            Assert.AreEqual(2, filled1.Fill.Order.RemainingQuantity);
            Assert.AreEqual(now2, filled1.Fill.Time);
            Assert.AreEqual(110, filled1.Fill.Price);
            Assert.AreEqual(3, filled1.Fill.Quantity);
            Assert.IsFalse(filled1.Fill.IsAggressor);

            var filled2 = events[2] as OrderFilledEvent;
            Assert.IsNotNull(filled2);
            Assert.AreEqual(id2, filled2.Fill.Order.Id);
            Assert.AreEqual(sec, filled2.Fill.Order.Security);
            Assert.AreEqual(now2, filled2.Fill.Order.CreatedTime);
            Assert.AreEqual(now2, filled2.Fill.Order.ModifiedTime);
            Assert.AreEqual(now2, filled2.Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, filled2.Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filled2.Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filled2.Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Sell, filled2.Fill.Order.Side);
            Assert.AreEqual(100, filled2.Fill.Order.Price);
            Assert.IsNull(filled2.Fill.Order.StopPrice);
            Assert.AreEqual(3, filled2.Fill.Order.Quantity);
            Assert.AreEqual(3, filled2.Fill.Order.FilledQuantity);
            Assert.AreEqual(0, filled2.Fill.Order.RemainingQuantity);
            Assert.AreEqual(now2, filled2.Fill.Time);
            Assert.AreEqual(110, filled2.Fill.Price);
            Assert.AreEqual(3, filled2.Fill.Quantity);
            Assert.IsTrue(filled2.Fill.IsAggressor);
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
            Assert.AreEqual(3, events.Count);

            var filled1 = events[1] as OrderFilledEvent;
            Assert.IsNotNull(filled1);
            Assert.AreEqual(id2, filled1.Fill.Order.Id);
            Assert.AreEqual(sec, filled1.Fill.Order.Security);
            Assert.AreEqual(now2, filled1.Fill.Order.CreatedTime);
            Assert.AreEqual(now2, filled1.Fill.Order.ModifiedTime);
            Assert.IsNull(filled1.Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, filled1.Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filled1.Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filled1.Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Buy, filled1.Fill.Order.Side);
            Assert.AreEqual(120, filled1.Fill.Order.Price);
            Assert.IsNull(filled1.Fill.Order.StopPrice);
            Assert.AreEqual(5, filled1.Fill.Order.Quantity);
            Assert.AreEqual(3, filled1.Fill.Order.FilledQuantity);
            Assert.AreEqual(2, filled1.Fill.Order.RemainingQuantity);
            Assert.AreEqual(now3, filled1.Fill.Time);
            Assert.AreEqual(120, filled1.Fill.Price);
            Assert.AreEqual(3, filled1.Fill.Quantity);
            Assert.IsFalse(filled1.Fill.IsAggressor);

            var filled2 = events[2] as OrderFilledEvent;
            Assert.IsNotNull(filled2);
            Assert.AreEqual(id3, filled2.Fill.Order.Id);
            Assert.AreEqual(sec, filled2.Fill.Order.Security);
            Assert.AreEqual(now3, filled2.Fill.Order.CreatedTime);
            Assert.AreEqual(now3, filled2.Fill.Order.ModifiedTime);
            Assert.AreEqual(now3, filled2.Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, filled2.Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filled2.Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filled2.Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Sell, filled2.Fill.Order.Side);
            Assert.AreEqual(100, filled2.Fill.Order.Price);
            Assert.IsNull(filled2.Fill.Order.StopPrice);
            Assert.AreEqual(3, filled2.Fill.Order.Quantity);
            Assert.AreEqual(3, filled2.Fill.Order.FilledQuantity);
            Assert.AreEqual(0, filled2.Fill.Order.RemainingQuantity);
            Assert.AreEqual(now3, filled2.Fill.Time);
            Assert.AreEqual(120, filled2.Fill.Price);
            Assert.AreEqual(3, filled2.Fill.Quantity);
            Assert.IsTrue(filled2.Fill.IsAggressor);
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
            Assert.AreEqual(5, events.Count);

            var filled1 = events[1] as OrderFilledEvent;
            Assert.IsNotNull(filled1);
            Assert.AreEqual(id2, filled1.Fill.Order.Id);
            Assert.AreEqual(sec, filled1.Fill.Order.Security);
            Assert.AreEqual(now2, filled1.Fill.Order.CreatedTime);
            Assert.AreEqual(now2, filled1.Fill.Order.ModifiedTime);
            Assert.AreEqual(now3, filled1.Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, filled1.Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filled1.Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filled1.Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Buy, filled1.Fill.Order.Side);
            Assert.AreEqual(120, filled1.Fill.Order.Price);
            Assert.IsNull(filled1.Fill.Order.StopPrice);
            Assert.AreEqual(5, filled1.Fill.Order.Quantity);
            Assert.AreEqual(5, filled1.Fill.Order.FilledQuantity);
            Assert.AreEqual(0, filled1.Fill.Order.RemainingQuantity);
            Assert.AreEqual(now3, filled1.Fill.Time);
            Assert.AreEqual(120, filled1.Fill.Price);
            Assert.AreEqual(5, filled1.Fill.Quantity);
            Assert.IsFalse(filled1.Fill.IsAggressor);

            var filled2 = events[2] as OrderFilledEvent;
            Assert.IsNotNull(filled2);
            Assert.AreEqual(id3, filled2.Fill.Order.Id);
            Assert.AreEqual(sec, filled2.Fill.Order.Security);
            Assert.AreEqual(now3, filled2.Fill.Order.CreatedTime);
            Assert.AreEqual(now3, filled2.Fill.Order.ModifiedTime);
            Assert.IsNull(filled2.Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, filled2.Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filled2.Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filled2.Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Sell, filled2.Fill.Order.Side);
            Assert.AreEqual(100, filled2.Fill.Order.Price);
            Assert.IsNull(filled2.Fill.Order.StopPrice);
            Assert.AreEqual(8, filled2.Fill.Order.Quantity);
            Assert.AreEqual(5, filled2.Fill.Order.FilledQuantity);
            Assert.AreEqual(3, filled2.Fill.Order.RemainingQuantity);
            Assert.AreEqual(now3, filled2.Fill.Time);
            Assert.AreEqual(120, filled2.Fill.Price);
            Assert.AreEqual(5, filled2.Fill.Quantity);
            Assert.IsTrue(filled2.Fill.IsAggressor);

            var filled3 = events[3] as OrderFilledEvent;
            Assert.IsNotNull(filled3);
            Assert.AreEqual(id1, filled3.Fill.Order.Id);
            Assert.AreEqual(sec, filled3.Fill.Order.Security);
            Assert.AreEqual(now1, filled3.Fill.Order.CreatedTime);
            Assert.AreEqual(now1, filled3.Fill.Order.ModifiedTime);
            Assert.IsNull(filled3.Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, filled3.Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filled3.Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filled3.Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Buy, filled3.Fill.Order.Side);
            Assert.AreEqual(110, filled3.Fill.Order.Price);
            Assert.IsNull(filled3.Fill.Order.StopPrice);
            Assert.AreEqual(5, filled3.Fill.Order.Quantity);
            Assert.AreEqual(3, filled3.Fill.Order.FilledQuantity);
            Assert.AreEqual(2, filled3.Fill.Order.RemainingQuantity);
            Assert.AreEqual(now3, filled3.Fill.Time);
            Assert.AreEqual(110, filled3.Fill.Price);
            Assert.AreEqual(3, filled3.Fill.Quantity);
            Assert.IsFalse(filled3.Fill.IsAggressor);

            var filled4 = events[4] as OrderFilledEvent;
            Assert.IsNotNull(filled4);
            Assert.AreEqual(id3, filled4.Fill.Order.Id);
            Assert.AreEqual(sec, filled4.Fill.Order.Security);
            Assert.AreEqual(now3, filled4.Fill.Order.CreatedTime);
            Assert.AreEqual(now3, filled4.Fill.Order.ModifiedTime);
            Assert.AreEqual(now3, filled4.Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, filled4.Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filled4.Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filled4.Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Sell, filled4.Fill.Order.Side);
            Assert.AreEqual(100, filled4.Fill.Order.Price);
            Assert.IsNull(filled4.Fill.Order.StopPrice);
            Assert.AreEqual(8, filled4.Fill.Order.Quantity);
            Assert.AreEqual(8, filled4.Fill.Order.FilledQuantity);
            Assert.AreEqual(0, filled4.Fill.Order.RemainingQuantity);
            Assert.AreEqual(now3, filled4.Fill.Time);
            Assert.AreEqual(110, filled4.Fill.Price);
            Assert.AreEqual(3, filled4.Fill.Quantity);
            Assert.IsTrue(filled4.Fill.IsAggressor);
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
            Assert.AreEqual(3, events.Count);

            var filled1 = events[1] as OrderFilledEvent;
            Assert.IsNotNull(filled1);
            Assert.AreEqual(id1, filled1.Fill.Order.Id);
            Assert.AreEqual(sec, filled1.Fill.Order.Security);
            Assert.AreEqual(now1, filled1.Fill.Order.CreatedTime);
            Assert.AreEqual(now1, filled1.Fill.Order.ModifiedTime);
            Assert.IsNull(filled1.Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, filled1.Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filled1.Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filled1.Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Buy, filled1.Fill.Order.Side);
            Assert.AreEqual(110, filled1.Fill.Order.Price);
            Assert.IsNull(filled1.Fill.Order.StopPrice);
            Assert.AreEqual(5, filled1.Fill.Order.Quantity);
            Assert.AreEqual(3, filled1.Fill.Order.FilledQuantity);
            Assert.AreEqual(2, filled1.Fill.Order.RemainingQuantity);
            Assert.AreEqual(now3, filled1.Fill.Time);
            Assert.AreEqual(110, filled1.Fill.Price);
            Assert.AreEqual(3, filled1.Fill.Quantity);
            Assert.IsFalse(filled1.Fill.IsAggressor);

            var filled2 = events[2] as OrderFilledEvent;
            Assert.IsNotNull(filled2);
            Assert.AreEqual(id3, filled2.Fill.Order.Id);
            Assert.AreEqual(sec, filled2.Fill.Order.Security);
            Assert.AreEqual(now3, filled2.Fill.Order.CreatedTime);
            Assert.AreEqual(now3, filled2.Fill.Order.ModifiedTime);
            Assert.AreEqual(now3, filled2.Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, filled2.Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filled2.Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filled2.Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Sell, filled2.Fill.Order.Side);
            Assert.AreEqual(100, filled2.Fill.Order.Price);
            Assert.IsNull(filled2.Fill.Order.StopPrice);
            Assert.AreEqual(3, filled2.Fill.Order.Quantity);
            Assert.AreEqual(3, filled2.Fill.Order.FilledQuantity);
            Assert.AreEqual(0, filled2.Fill.Order.RemainingQuantity);
            Assert.AreEqual(now3, filled2.Fill.Time);
            Assert.AreEqual(110, filled2.Fill.Price);
            Assert.AreEqual(3, filled2.Fill.Quantity);
            Assert.IsTrue(filled2.Fill.IsAggressor);
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
            Assert.AreEqual(5, events.Count);

            var filled1 = events[1] as OrderFilledEvent;
            Assert.IsNotNull(filled1);
            Assert.AreEqual(id1, filled1.Fill.Order.Id);
            Assert.AreEqual(sec, filled1.Fill.Order.Security);
            Assert.AreEqual(now1, filled1.Fill.Order.CreatedTime);
            Assert.AreEqual(now1, filled1.Fill.Order.ModifiedTime);
            Assert.AreEqual(now3, filled1.Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, filled1.Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filled1.Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filled1.Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Buy, filled1.Fill.Order.Side);
            Assert.AreEqual(110, filled1.Fill.Order.Price);
            Assert.IsNull(filled1.Fill.Order.StopPrice);
            Assert.AreEqual(5, filled1.Fill.Order.Quantity);
            Assert.AreEqual(5, filled1.Fill.Order.FilledQuantity);
            Assert.AreEqual(0, filled1.Fill.Order.RemainingQuantity);
            Assert.AreEqual(now3, filled1.Fill.Time);
            Assert.AreEqual(110, filled1.Fill.Price);
            Assert.AreEqual(5, filled1.Fill.Quantity);
            Assert.IsFalse(filled1.Fill.IsAggressor);

            var filled2 = events[2] as OrderFilledEvent;
            Assert.IsNotNull(filled2);
            Assert.AreEqual(id3, filled2.Fill.Order.Id);
            Assert.AreEqual(sec, filled2.Fill.Order.Security);
            Assert.AreEqual(now3, filled2.Fill.Order.CreatedTime);
            Assert.AreEqual(now3, filled2.Fill.Order.ModifiedTime);
            Assert.IsNull(filled2.Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, filled2.Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filled2.Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filled2.Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Sell, filled2.Fill.Order.Side);
            Assert.AreEqual(100, filled2.Fill.Order.Price);
            Assert.IsNull(filled2.Fill.Order.StopPrice);
            Assert.AreEqual(8, filled2.Fill.Order.Quantity);
            Assert.AreEqual(5, filled2.Fill.Order.FilledQuantity);
            Assert.AreEqual(3, filled2.Fill.Order.RemainingQuantity);
            Assert.AreEqual(now3, filled2.Fill.Time);
            Assert.AreEqual(110, filled2.Fill.Price);
            Assert.AreEqual(5, filled2.Fill.Quantity);
            Assert.IsTrue(filled2.Fill.IsAggressor);

            var filled3 = events[3] as OrderFilledEvent;
            Assert.IsNotNull(filled3);
            Assert.AreEqual(id2, filled3.Fill.Order.Id);
            Assert.AreEqual(sec, filled3.Fill.Order.Security);
            Assert.AreEqual(now2, filled3.Fill.Order.CreatedTime);
            Assert.AreEqual(now2, filled3.Fill.Order.ModifiedTime);
            Assert.IsNull(filled3.Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, filled3.Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filled3.Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filled3.Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Buy, filled3.Fill.Order.Side);
            Assert.AreEqual(110, filled3.Fill.Order.Price);
            Assert.IsNull(filled3.Fill.Order.StopPrice);
            Assert.AreEqual(5, filled3.Fill.Order.Quantity);
            Assert.AreEqual(3, filled3.Fill.Order.FilledQuantity);
            Assert.AreEqual(2, filled3.Fill.Order.RemainingQuantity);
            Assert.AreEqual(now3, filled3.Fill.Time);
            Assert.AreEqual(110, filled3.Fill.Price);
            Assert.AreEqual(3, filled3.Fill.Quantity);
            Assert.IsFalse(filled3.Fill.IsAggressor);

            var filled4 = events[4] as OrderFilledEvent;
            Assert.IsNotNull(filled4);
            Assert.AreEqual(id3, filled4.Fill.Order.Id);
            Assert.AreEqual(sec, filled4.Fill.Order.Security);
            Assert.AreEqual(now3, filled4.Fill.Order.CreatedTime);
            Assert.AreEqual(now3, filled4.Fill.Order.ModifiedTime);
            Assert.AreEqual(now3, filled4.Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, filled4.Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filled4.Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filled4.Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Sell, filled4.Fill.Order.Side);
            Assert.AreEqual(100, filled4.Fill.Order.Price);
            Assert.IsNull(filled4.Fill.Order.StopPrice);
            Assert.AreEqual(8, filled4.Fill.Order.Quantity);
            Assert.AreEqual(8, filled4.Fill.Order.FilledQuantity);
            Assert.AreEqual(0, filled4.Fill.Order.RemainingQuantity);
            Assert.AreEqual(now3, filled4.Fill.Time);
            Assert.AreEqual(110, filled4.Fill.Price);
            Assert.AreEqual(3, filled4.Fill.Quantity);
            Assert.IsTrue(filled4.Fill.IsAggressor);
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
            Assert.AreEqual(3, events.Count);

            var filled1 = events[1] as OrderFilledEvent;
            Assert.IsNotNull(filled1);
            Assert.AreEqual(id2, filled1.Fill.Order.Id);
            Assert.AreEqual(sec, filled1.Fill.Order.Security);
            Assert.AreEqual(now2, filled1.Fill.Order.CreatedTime);
            Assert.AreEqual(now2, filled1.Fill.Order.ModifiedTime);
            Assert.IsNull(filled1.Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, filled1.Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filled1.Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filled1.Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Sell, filled1.Fill.Order.Side);
            Assert.AreEqual(80, filled1.Fill.Order.Price);
            Assert.IsNull(filled1.Fill.Order.StopPrice);
            Assert.AreEqual(5, filled1.Fill.Order.Quantity);
            Assert.AreEqual(3, filled1.Fill.Order.FilledQuantity);
            Assert.AreEqual(2, filled1.Fill.Order.RemainingQuantity);
            Assert.AreEqual(now3, filled1.Fill.Time);
            Assert.AreEqual(80, filled1.Fill.Price);
            Assert.AreEqual(3, filled1.Fill.Quantity);
            Assert.IsFalse(filled1.Fill.IsAggressor);

            var filled2 = events[2] as OrderFilledEvent;
            Assert.IsNotNull(filled2);
            Assert.AreEqual(id3, filled2.Fill.Order.Id);
            Assert.AreEqual(sec, filled2.Fill.Order.Security);
            Assert.AreEqual(now3, filled2.Fill.Order.CreatedTime);
            Assert.AreEqual(now3, filled2.Fill.Order.ModifiedTime);
            Assert.AreEqual(now3, filled2.Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, filled2.Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filled2.Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filled2.Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Buy, filled2.Fill.Order.Side);
            Assert.AreEqual(100, filled2.Fill.Order.Price);
            Assert.IsNull(filled2.Fill.Order.StopPrice);
            Assert.AreEqual(3, filled2.Fill.Order.Quantity);
            Assert.AreEqual(3, filled2.Fill.Order.FilledQuantity);
            Assert.AreEqual(0, filled2.Fill.Order.RemainingQuantity);
            Assert.AreEqual(now3, filled2.Fill.Time);
            Assert.AreEqual(80, filled2.Fill.Price);
            Assert.AreEqual(3, filled2.Fill.Quantity);
            Assert.IsTrue(filled2.Fill.IsAggressor);
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
            Assert.AreEqual(5, events.Count);

            var filled1 = events[1] as OrderFilledEvent;
            Assert.IsNotNull(filled1);
            Assert.AreEqual(id2, filled1.Fill.Order.Id);
            Assert.AreEqual(sec, filled1.Fill.Order.Security);
            Assert.AreEqual(now2, filled1.Fill.Order.CreatedTime);
            Assert.AreEqual(now2, filled1.Fill.Order.ModifiedTime);
            Assert.AreEqual(now3, filled1.Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, filled1.Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filled1.Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filled1.Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Sell, filled1.Fill.Order.Side);
            Assert.AreEqual(80, filled1.Fill.Order.Price);
            Assert.IsNull(filled1.Fill.Order.StopPrice);
            Assert.AreEqual(5, filled1.Fill.Order.Quantity);
            Assert.AreEqual(5, filled1.Fill.Order.FilledQuantity);
            Assert.AreEqual(0, filled1.Fill.Order.RemainingQuantity);
            Assert.AreEqual(now3, filled1.Fill.Time);
            Assert.AreEqual(80, filled1.Fill.Price);
            Assert.AreEqual(5, filled1.Fill.Quantity);
            Assert.IsFalse(filled1.Fill.IsAggressor);

            var filled2 = events[2] as OrderFilledEvent;
            Assert.IsNotNull(filled2);
            Assert.AreEqual(id3, filled2.Fill.Order.Id);
            Assert.AreEqual(sec, filled2.Fill.Order.Security);
            Assert.AreEqual(now3, filled2.Fill.Order.CreatedTime);
            Assert.AreEqual(now3, filled2.Fill.Order.ModifiedTime);
            Assert.IsNull(filled2.Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, filled2.Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filled2.Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filled2.Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Buy, filled2.Fill.Order.Side);
            Assert.AreEqual(100, filled2.Fill.Order.Price);
            Assert.IsNull(filled2.Fill.Order.StopPrice);
            Assert.AreEqual(8, filled2.Fill.Order.Quantity);
            Assert.AreEqual(5, filled2.Fill.Order.FilledQuantity);
            Assert.AreEqual(3, filled2.Fill.Order.RemainingQuantity);
            Assert.AreEqual(now3, filled2.Fill.Time);
            Assert.AreEqual(80, filled2.Fill.Price);
            Assert.AreEqual(5, filled2.Fill.Quantity);
            Assert.IsTrue(filled2.Fill.IsAggressor);

            var filled3 = events[3] as OrderFilledEvent;
            Assert.IsNotNull(filled3);
            Assert.AreEqual(id1, filled3.Fill.Order.Id);
            Assert.AreEqual(sec, filled3.Fill.Order.Security);
            Assert.AreEqual(now1, filled3.Fill.Order.CreatedTime);
            Assert.AreEqual(now1, filled3.Fill.Order.ModifiedTime);
            Assert.IsNull(filled3.Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, filled3.Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filled3.Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filled3.Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Sell, filled3.Fill.Order.Side);
            Assert.AreEqual(90, filled3.Fill.Order.Price);
            Assert.IsNull(filled3.Fill.Order.StopPrice);
            Assert.AreEqual(5, filled3.Fill.Order.Quantity);
            Assert.AreEqual(3, filled3.Fill.Order.FilledQuantity);
            Assert.AreEqual(2, filled3.Fill.Order.RemainingQuantity);
            Assert.AreEqual(now3, filled3.Fill.Time);
            Assert.AreEqual(90, filled3.Fill.Price);
            Assert.AreEqual(3, filled3.Fill.Quantity);
            Assert.IsFalse(filled3.Fill.IsAggressor);

            var filled4 = events[4] as OrderFilledEvent;
            Assert.IsNotNull(filled4);
            Assert.AreEqual(id3, filled4.Fill.Order.Id);
            Assert.AreEqual(sec, filled4.Fill.Order.Security);
            Assert.AreEqual(now3, filled4.Fill.Order.CreatedTime);
            Assert.AreEqual(now3, filled4.Fill.Order.ModifiedTime);
            Assert.AreEqual(now3, filled4.Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, filled4.Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filled4.Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filled4.Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Buy, filled4.Fill.Order.Side);
            Assert.AreEqual(100, filled4.Fill.Order.Price);
            Assert.IsNull(filled4.Fill.Order.StopPrice);
            Assert.AreEqual(8, filled4.Fill.Order.Quantity);
            Assert.AreEqual(8, filled4.Fill.Order.FilledQuantity);
            Assert.AreEqual(0, filled4.Fill.Order.RemainingQuantity);
            Assert.AreEqual(now3, filled4.Fill.Time);
            Assert.AreEqual(90, filled4.Fill.Price);
            Assert.AreEqual(3, filled4.Fill.Quantity);
            Assert.IsTrue(filled4.Fill.IsAggressor);
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
            Assert.AreEqual(3, events.Count);

            var filled1 = events[1] as OrderFilledEvent;
            Assert.IsNotNull(filled1);
            Assert.AreEqual(id1, filled1.Fill.Order.Id);
            Assert.AreEqual(sec, filled1.Fill.Order.Security);
            Assert.AreEqual(now1, filled1.Fill.Order.CreatedTime);
            Assert.AreEqual(now1, filled1.Fill.Order.ModifiedTime);
            Assert.IsNull(filled1.Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, filled1.Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filled1.Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filled1.Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Sell, filled1.Fill.Order.Side);
            Assert.AreEqual(90, filled1.Fill.Order.Price);
            Assert.IsNull(filled1.Fill.Order.StopPrice);
            Assert.AreEqual(5, filled1.Fill.Order.Quantity);
            Assert.AreEqual(3, filled1.Fill.Order.FilledQuantity);
            Assert.AreEqual(2, filled1.Fill.Order.RemainingQuantity);
            Assert.AreEqual(now3, filled1.Fill.Time);
            Assert.AreEqual(90, filled1.Fill.Price);
            Assert.AreEqual(3, filled1.Fill.Quantity);
            Assert.IsFalse(filled1.Fill.IsAggressor);

            var filled2 = events[2] as OrderFilledEvent;
            Assert.IsNotNull(filled2);
            Assert.AreEqual(id3, filled2.Fill.Order.Id);
            Assert.AreEqual(sec, filled2.Fill.Order.Security);
            Assert.AreEqual(now3, filled2.Fill.Order.CreatedTime);
            Assert.AreEqual(now3, filled2.Fill.Order.ModifiedTime);
            Assert.AreEqual(now3, filled2.Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, filled2.Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filled2.Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filled2.Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Buy, filled2.Fill.Order.Side);
            Assert.AreEqual(100, filled2.Fill.Order.Price);
            Assert.IsNull(filled2.Fill.Order.StopPrice);
            Assert.AreEqual(3, filled2.Fill.Order.Quantity);
            Assert.AreEqual(3, filled2.Fill.Order.FilledQuantity);
            Assert.AreEqual(0, filled2.Fill.Order.RemainingQuantity);
            Assert.AreEqual(now3, filled2.Fill.Time);
            Assert.AreEqual(90, filled2.Fill.Price);
            Assert.AreEqual(3, filled2.Fill.Quantity);
            Assert.IsTrue(filled2.Fill.IsAggressor);
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
            Assert.AreEqual(5, events.Count);

            var filled1 = events[1] as OrderFilledEvent;
            Assert.IsNotNull(filled1);
            Assert.AreEqual(id1, filled1.Fill.Order.Id);
            Assert.AreEqual(sec, filled1.Fill.Order.Security);
            Assert.AreEqual(now1, filled1.Fill.Order.CreatedTime);
            Assert.AreEqual(now1, filled1.Fill.Order.ModifiedTime);
            Assert.AreEqual(now3, filled1.Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, filled1.Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filled1.Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filled1.Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Sell, filled1.Fill.Order.Side);
            Assert.AreEqual(90, filled1.Fill.Order.Price);
            Assert.IsNull(filled1.Fill.Order.StopPrice);
            Assert.AreEqual(5, filled1.Fill.Order.Quantity);
            Assert.AreEqual(5, filled1.Fill.Order.FilledQuantity);
            Assert.AreEqual(0, filled1.Fill.Order.RemainingQuantity);
            Assert.AreEqual(now3, filled1.Fill.Time);
            Assert.AreEqual(90, filled1.Fill.Price);
            Assert.AreEqual(5, filled1.Fill.Quantity);
            Assert.IsFalse(filled1.Fill.IsAggressor);

            var filled2 = events[2] as OrderFilledEvent;
            Assert.IsNotNull(filled2);
            Assert.AreEqual(id3, filled2.Fill.Order.Id);
            Assert.AreEqual(sec, filled2.Fill.Order.Security);
            Assert.AreEqual(now3, filled2.Fill.Order.CreatedTime);
            Assert.AreEqual(now3, filled2.Fill.Order.ModifiedTime);
            Assert.IsNull(filled2.Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, filled2.Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filled2.Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filled2.Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Buy, filled2.Fill.Order.Side);
            Assert.AreEqual(100, filled2.Fill.Order.Price);
            Assert.IsNull(filled2.Fill.Order.StopPrice);
            Assert.AreEqual(8, filled2.Fill.Order.Quantity);
            Assert.AreEqual(5, filled2.Fill.Order.FilledQuantity);
            Assert.AreEqual(3, filled2.Fill.Order.RemainingQuantity);
            Assert.AreEqual(now3, filled2.Fill.Time);
            Assert.AreEqual(90, filled2.Fill.Price);
            Assert.AreEqual(5, filled2.Fill.Quantity);
            Assert.IsTrue(filled2.Fill.IsAggressor);

            var filled3 = events[3] as OrderFilledEvent;
            Assert.IsNotNull(filled3);
            Assert.AreEqual(id2, filled3.Fill.Order.Id);
            Assert.AreEqual(sec, filled3.Fill.Order.Security);
            Assert.AreEqual(now2, filled3.Fill.Order.CreatedTime);
            Assert.AreEqual(now2, filled3.Fill.Order.ModifiedTime);
            Assert.IsNull(filled3.Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, filled3.Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filled3.Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filled3.Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Sell, filled3.Fill.Order.Side);
            Assert.AreEqual(90, filled3.Fill.Order.Price);
            Assert.IsNull(filled3.Fill.Order.StopPrice);
            Assert.AreEqual(5, filled3.Fill.Order.Quantity);
            Assert.AreEqual(3, filled3.Fill.Order.FilledQuantity);
            Assert.AreEqual(2, filled3.Fill.Order.RemainingQuantity);
            Assert.AreEqual(now3, filled3.Fill.Time);
            Assert.AreEqual(90, filled3.Fill.Price);
            Assert.AreEqual(3, filled3.Fill.Quantity);
            Assert.IsFalse(filled3.Fill.IsAggressor);

            var filled4 = events[4] as OrderFilledEvent;
            Assert.IsNotNull(filled4);
            Assert.AreEqual(id3, filled4.Fill.Order.Id);
            Assert.AreEqual(sec, filled4.Fill.Order.Security);
            Assert.AreEqual(now3, filled4.Fill.Order.CreatedTime);
            Assert.AreEqual(now3, filled4.Fill.Order.ModifiedTime);
            Assert.AreEqual(now3, filled4.Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, filled4.Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filled4.Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filled4.Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Buy, filled4.Fill.Order.Side);
            Assert.AreEqual(100, filled4.Fill.Order.Price);
            Assert.IsNull(filled4.Fill.Order.StopPrice);
            Assert.AreEqual(8, filled4.Fill.Order.Quantity);
            Assert.AreEqual(8, filled4.Fill.Order.FilledQuantity);
            Assert.AreEqual(0, filled4.Fill.Order.RemainingQuantity);
            Assert.AreEqual(now3, filled4.Fill.Time);
            Assert.AreEqual(90, filled4.Fill.Price);
            Assert.AreEqual(3, filled4.Fill.Quantity);
            Assert.IsTrue(filled4.Fill.IsAggressor);
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
            Assert.AreEqual(3, events.Count);

            var filled1 = events[1] as OrderFilledEvent;
            Assert.IsNotNull(filled1);
            Assert.AreEqual(id2, filled1.Fill.Order.Id);
            Assert.AreEqual(sec, filled1.Fill.Order.Security);
            Assert.AreEqual(now2, filled1.Fill.Order.CreatedTime);
            Assert.AreEqual(now2, filled1.Fill.Order.ModifiedTime);
            Assert.IsNull(filled1.Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, filled1.Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filled1.Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filled1.Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Buy, filled1.Fill.Order.Side);
            Assert.AreEqual(110, filled1.Fill.Order.Price);
            Assert.IsNull(filled1.Fill.Order.StopPrice);
            Assert.AreEqual(5, filled1.Fill.Order.Quantity);
            Assert.AreEqual(3, filled1.Fill.Order.FilledQuantity);
            Assert.AreEqual(2, filled1.Fill.Order.RemainingQuantity);
            Assert.AreEqual(now4, filled1.Fill.Time);
            Assert.AreEqual(110, filled1.Fill.Price);
            Assert.AreEqual(3, filled1.Fill.Quantity);
            Assert.IsFalse(filled1.Fill.IsAggressor);

            var filled2 = events[2] as OrderFilledEvent;
            Assert.IsNotNull(filled2);
            Assert.AreEqual(id3, filled2.Fill.Order.Id);
            Assert.AreEqual(sec, filled2.Fill.Order.Security);
            Assert.AreEqual(now4, filled2.Fill.Order.CreatedTime);
            Assert.AreEqual(now4, filled2.Fill.Order.ModifiedTime);
            Assert.AreEqual(now4, filled2.Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, filled2.Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filled2.Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filled2.Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Sell, filled2.Fill.Order.Side);
            Assert.AreEqual(100, filled2.Fill.Order.Price);
            Assert.IsNull(filled2.Fill.Order.StopPrice);
            Assert.AreEqual(3, filled2.Fill.Order.Quantity);
            Assert.AreEqual(3, filled2.Fill.Order.FilledQuantity);
            Assert.AreEqual(0, filled2.Fill.Order.RemainingQuantity);
            Assert.AreEqual(now4, filled2.Fill.Time);
            Assert.AreEqual(110, filled2.Fill.Price);
            Assert.AreEqual(3, filled2.Fill.Quantity);
            Assert.IsTrue(filled2.Fill.IsAggressor);
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
            Assert.AreEqual(5, events.Count);

            var filled1 = events[1] as OrderFilledEvent;
            Assert.IsNotNull(filled1);
            Assert.AreEqual(id2, filled1.Fill.Order.Id);
            Assert.AreEqual(sec, filled1.Fill.Order.Security);
            Assert.AreEqual(now2, filled1.Fill.Order.CreatedTime);
            Assert.AreEqual(now2, filled1.Fill.Order.ModifiedTime);
            Assert.AreEqual(now4, filled1.Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, filled1.Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filled1.Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filled1.Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Buy, filled1.Fill.Order.Side);
            Assert.AreEqual(110, filled1.Fill.Order.Price);
            Assert.IsNull(filled1.Fill.Order.StopPrice);
            Assert.AreEqual(5, filled1.Fill.Order.Quantity);
            Assert.AreEqual(5, filled1.Fill.Order.FilledQuantity);
            Assert.AreEqual(0, filled1.Fill.Order.RemainingQuantity);
            Assert.AreEqual(now4, filled1.Fill.Time);
            Assert.AreEqual(110, filled1.Fill.Price);
            Assert.AreEqual(5, filled1.Fill.Quantity);
            Assert.IsFalse(filled1.Fill.IsAggressor);

            var filled2 = events[2] as OrderFilledEvent;
            Assert.IsNotNull(filled2);
            Assert.AreEqual(id3, filled2.Fill.Order.Id);
            Assert.AreEqual(sec, filled2.Fill.Order.Security);
            Assert.AreEqual(now4, filled2.Fill.Order.CreatedTime);
            Assert.AreEqual(now4, filled2.Fill.Order.ModifiedTime);
            Assert.IsNull(filled2.Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, filled2.Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filled2.Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filled2.Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Sell, filled2.Fill.Order.Side);
            Assert.AreEqual(100, filled2.Fill.Order.Price);
            Assert.IsNull(filled2.Fill.Order.StopPrice);
            Assert.AreEqual(8, filled2.Fill.Order.Quantity);
            Assert.AreEqual(5, filled2.Fill.Order.FilledQuantity);
            Assert.AreEqual(3, filled2.Fill.Order.RemainingQuantity);
            Assert.AreEqual(now4, filled2.Fill.Time);
            Assert.AreEqual(110, filled2.Fill.Price);
            Assert.AreEqual(5, filled2.Fill.Quantity);
            Assert.IsTrue(filled2.Fill.IsAggressor);

            var filled3 = events[3] as OrderFilledEvent;
            Assert.IsNotNull(filled3);
            Assert.AreEqual(id1, filled3.Fill.Order.Id);
            Assert.AreEqual(sec, filled3.Fill.Order.Security);
            Assert.AreEqual(now1, filled3.Fill.Order.CreatedTime);
            Assert.AreEqual(now3, filled3.Fill.Order.ModifiedTime);
            Assert.IsNull(filled3.Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, filled3.Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filled3.Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filled3.Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Buy, filled3.Fill.Order.Side);
            Assert.AreEqual(110, filled3.Fill.Order.Price);
            Assert.IsNull(filled3.Fill.Order.StopPrice);
            Assert.AreEqual(6, filled3.Fill.Order.Quantity);
            Assert.AreEqual(3, filled3.Fill.Order.FilledQuantity);
            Assert.AreEqual(3, filled3.Fill.Order.RemainingQuantity);
            Assert.AreEqual(now4, filled3.Fill.Time);
            Assert.AreEqual(110, filled3.Fill.Price);
            Assert.AreEqual(3, filled3.Fill.Quantity);
            Assert.IsFalse(filled3.Fill.IsAggressor);

            var filled4 = events[4] as OrderFilledEvent;
            Assert.IsNotNull(filled4);
            Assert.AreEqual(id3, filled4.Fill.Order.Id);
            Assert.AreEqual(sec, filled4.Fill.Order.Security);
            Assert.AreEqual(now4, filled4.Fill.Order.CreatedTime);
            Assert.AreEqual(now4, filled4.Fill.Order.ModifiedTime);
            Assert.AreEqual(now4, filled4.Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, filled4.Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filled4.Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filled4.Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Sell, filled4.Fill.Order.Side);
            Assert.AreEqual(100, filled4.Fill.Order.Price);
            Assert.IsNull(filled4.Fill.Order.StopPrice);
            Assert.AreEqual(8, filled4.Fill.Order.Quantity);
            Assert.AreEqual(8, filled4.Fill.Order.FilledQuantity);
            Assert.AreEqual(0, filled4.Fill.Order.RemainingQuantity);
            Assert.AreEqual(now4, filled4.Fill.Time);
            Assert.AreEqual(110, filled4.Fill.Price);
            Assert.AreEqual(3, filled4.Fill.Quantity);
            Assert.IsTrue(filled4.Fill.IsAggressor);
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
            Assert.AreEqual(3, events.Count);

            var filled1 = events[1] as OrderFilledEvent;
            Assert.IsNotNull(filled1);
            Assert.AreEqual(id1, filled1.Fill.Order.Id);
            Assert.AreEqual(sec, filled1.Fill.Order.Security);
            Assert.AreEqual(now1, filled1.Fill.Order.CreatedTime);
            Assert.AreEqual(now3, filled1.Fill.Order.ModifiedTime);
            Assert.IsNull(filled1.Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, filled1.Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filled1.Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filled1.Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Buy, filled1.Fill.Order.Side);
            Assert.AreEqual(110, filled1.Fill.Order.Price);
            Assert.IsNull(filled1.Fill.Order.StopPrice);
            Assert.AreEqual(4, filled1.Fill.Order.Quantity);
            Assert.AreEqual(3, filled1.Fill.Order.FilledQuantity);
            Assert.AreEqual(1, filled1.Fill.Order.RemainingQuantity);
            Assert.AreEqual(now4, filled1.Fill.Time);
            Assert.AreEqual(110, filled1.Fill.Price);
            Assert.AreEqual(3, filled1.Fill.Quantity);
            Assert.IsFalse(filled1.Fill.IsAggressor);

            var filled2 = events[2] as OrderFilledEvent;
            Assert.IsNotNull(filled2);
            Assert.AreEqual(id3, filled2.Fill.Order.Id);
            Assert.AreEqual(sec, filled2.Fill.Order.Security);
            Assert.AreEqual(now4, filled2.Fill.Order.CreatedTime);
            Assert.AreEqual(now4, filled2.Fill.Order.ModifiedTime);
            Assert.AreEqual(now4, filled2.Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, filled2.Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filled2.Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filled2.Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Sell, filled2.Fill.Order.Side);
            Assert.AreEqual(100, filled2.Fill.Order.Price);
            Assert.IsNull(filled2.Fill.Order.StopPrice);
            Assert.AreEqual(3, filled2.Fill.Order.Quantity);
            Assert.AreEqual(3, filled2.Fill.Order.FilledQuantity);
            Assert.AreEqual(0, filled2.Fill.Order.RemainingQuantity);
            Assert.AreEqual(now4, filled2.Fill.Time);
            Assert.AreEqual(110, filled2.Fill.Price);
            Assert.AreEqual(3, filled2.Fill.Quantity);
            Assert.IsTrue(filled2.Fill.IsAggressor);
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
            Assert.AreEqual(5, events.Count);

            var filled1 = events[1] as OrderFilledEvent;
            Assert.IsNotNull(filled1);
            Assert.AreEqual(id1, filled1.Fill.Order.Id);
            Assert.AreEqual(sec, filled1.Fill.Order.Security);
            Assert.AreEqual(now1, filled1.Fill.Order.CreatedTime);
            Assert.AreEqual(now3, filled1.Fill.Order.ModifiedTime);
            Assert.AreEqual(now4, filled1.Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, filled1.Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filled1.Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filled1.Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Buy, filled1.Fill.Order.Side);
            Assert.AreEqual(110, filled1.Fill.Order.Price);
            Assert.IsNull(filled1.Fill.Order.StopPrice);
            Assert.AreEqual(4, filled1.Fill.Order.Quantity);
            Assert.AreEqual(4, filled1.Fill.Order.FilledQuantity);
            Assert.AreEqual(0, filled1.Fill.Order.RemainingQuantity);
            Assert.AreEqual(now4, filled1.Fill.Time);
            Assert.AreEqual(110, filled1.Fill.Price);
            Assert.AreEqual(4, filled1.Fill.Quantity);
            Assert.IsFalse(filled1.Fill.IsAggressor);

            var filled2 = events[2] as OrderFilledEvent;
            Assert.IsNotNull(filled2);
            Assert.AreEqual(id3, filled2.Fill.Order.Id);
            Assert.AreEqual(sec, filled2.Fill.Order.Security);
            Assert.AreEqual(now4, filled2.Fill.Order.CreatedTime);
            Assert.AreEqual(now4, filled2.Fill.Order.ModifiedTime);
            Assert.IsNull(filled2.Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, filled2.Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filled2.Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filled2.Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Sell, filled2.Fill.Order.Side);
            Assert.AreEqual(100, filled2.Fill.Order.Price);
            Assert.IsNull(filled2.Fill.Order.StopPrice);
            Assert.AreEqual(8, filled2.Fill.Order.Quantity);
            Assert.AreEqual(4, filled2.Fill.Order.FilledQuantity);
            Assert.AreEqual(4, filled2.Fill.Order.RemainingQuantity);
            Assert.AreEqual(now4, filled2.Fill.Time);
            Assert.AreEqual(110, filled2.Fill.Price);
            Assert.AreEqual(4, filled2.Fill.Quantity);
            Assert.IsTrue(filled2.Fill.IsAggressor);

            var filled3 = events[3] as OrderFilledEvent;
            Assert.IsNotNull(filled3);
            Assert.AreEqual(id2, filled3.Fill.Order.Id);
            Assert.AreEqual(sec, filled3.Fill.Order.Security);
            Assert.AreEqual(now2, filled3.Fill.Order.CreatedTime);
            Assert.AreEqual(now2, filled3.Fill.Order.ModifiedTime);
            Assert.IsNull(filled3.Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, filled3.Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filled3.Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filled3.Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Buy, filled3.Fill.Order.Side);
            Assert.AreEqual(110, filled3.Fill.Order.Price);
            Assert.IsNull(filled3.Fill.Order.StopPrice);
            Assert.AreEqual(5, filled3.Fill.Order.Quantity);
            Assert.AreEqual(4, filled3.Fill.Order.FilledQuantity);
            Assert.AreEqual(1, filled3.Fill.Order.RemainingQuantity);
            Assert.AreEqual(now4, filled3.Fill.Time);
            Assert.AreEqual(110, filled3.Fill.Price);
            Assert.AreEqual(4, filled3.Fill.Quantity);
            Assert.IsFalse(filled3.Fill.IsAggressor);

            var filled4 = events[4] as OrderFilledEvent;
            Assert.IsNotNull(filled4);
            Assert.AreEqual(id3, filled4.Fill.Order.Id);
            Assert.AreEqual(sec, filled4.Fill.Order.Security);
            Assert.AreEqual(now4, filled4.Fill.Order.CreatedTime);
            Assert.AreEqual(now4, filled4.Fill.Order.ModifiedTime);
            Assert.AreEqual(now4, filled4.Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, filled4.Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filled4.Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filled4.Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Sell, filled4.Fill.Order.Side);
            Assert.AreEqual(100, filled4.Fill.Order.Price);
            Assert.IsNull(filled4.Fill.Order.StopPrice);
            Assert.AreEqual(8, filled4.Fill.Order.Quantity);
            Assert.AreEqual(8, filled4.Fill.Order.FilledQuantity);
            Assert.AreEqual(0, filled4.Fill.Order.RemainingQuantity);
            Assert.AreEqual(now4, filled4.Fill.Time);
            Assert.AreEqual(110, filled4.Fill.Price);
            Assert.AreEqual(4, filled4.Fill.Quantity);
            Assert.IsTrue(filled4.Fill.IsAggressor);
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
            Assert.AreEqual(3, events.Count);

            var filled1 = events[1] as OrderFilledEvent;
            Assert.IsNotNull(filled1);
            Assert.AreEqual(id1, filled1.Fill.Order.Id);
            Assert.AreEqual(sec, filled1.Fill.Order.Security);
            Assert.AreEqual(now1, filled1.Fill.Order.CreatedTime);
            Assert.AreEqual(now1, filled1.Fill.Order.ModifiedTime);
            Assert.AreEqual(now2, filled1.Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, filled1.Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filled1.Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filled1.Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Buy, filled1.Fill.Order.Side);
            Assert.AreEqual(500, filled1.Fill.Order.Price);
            Assert.IsNull(filled1.Fill.Order.StopPrice);
            Assert.AreEqual(3, filled1.Fill.Order.Quantity);
            Assert.AreEqual(3, filled1.Fill.Order.FilledQuantity);
            Assert.AreEqual(0, filled1.Fill.Order.RemainingQuantity);
            Assert.AreEqual(now2, filled1.Fill.Time);
            Assert.AreEqual(500, filled1.Fill.Price);
            Assert.AreEqual(3, filled1.Fill.Quantity);
            Assert.IsFalse(filled1.Fill.IsAggressor);

            var filled2 = events[2] as OrderFilledEvent;
            Assert.IsNotNull(filled2);
            Assert.AreEqual(id2, filled2.Fill.Order.Id);
            Assert.AreEqual(sec, filled2.Fill.Order.Security);
            Assert.AreEqual(now2, filled2.Fill.Order.CreatedTime);
            Assert.AreEqual(now2, filled2.Fill.Order.ModifiedTime);
            Assert.IsNull(filled2.Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, filled2.Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filled2.Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filled2.Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Sell, filled2.Fill.Order.Side);
            Assert.AreEqual(300, filled2.Fill.Order.Price);
            Assert.IsNull(filled2.Fill.Order.StopPrice);
            Assert.AreEqual(5, filled2.Fill.Order.Quantity);
            Assert.AreEqual(3, filled2.Fill.Order.FilledQuantity);
            Assert.AreEqual(2, filled2.Fill.Order.RemainingQuantity);
            Assert.AreEqual(now2, filled2.Fill.Time);
            Assert.AreEqual(500, filled2.Fill.Price);
            Assert.AreEqual(3, filled2.Fill.Quantity);
            Assert.IsTrue(filled2.Fill.IsAggressor);
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