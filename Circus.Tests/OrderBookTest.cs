using System;
using System.Collections.Generic;
using Circus.Enums;
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

            OrderCreatedSuccessEventArgs createdArgs = null;
            book.OrderCreated += (sender, e) => createdArgs = e;
            book.SetStatus(OrderBookStatus.Open);
            var id = Guid.NewGuid();

            // act
            book.CreateLimitOrder(id, TimeInForce.Day, Side.Buy, 100, 3);

            // assert
            Assert.IsNotNull(createdArgs);
            Assert.AreEqual(id, createdArgs.Order.Id);
            Assert.AreEqual(sec, createdArgs.Order.Security);
            Assert.AreEqual(now, createdArgs.Order.CreatedTime);
            Assert.AreEqual(now, createdArgs.Order.ModifiedTime);
            Assert.IsNull(createdArgs.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, createdArgs.Order.Status);
            Assert.AreEqual(OrderType.Limit, createdArgs.Order.Type);
            Assert.AreEqual(TimeInForce.Day, createdArgs.Order.TimeInForce);
            Assert.AreEqual(Side.Buy, createdArgs.Order.Side);
            Assert.AreEqual(100, createdArgs.Order.Price);
            Assert.IsNull(createdArgs.Order.StopPrice);
            Assert.AreEqual(3, createdArgs.Order.Quantity);
            Assert.AreEqual(0, createdArgs.Order.FilledQuantity);
            Assert.AreEqual(3, createdArgs.Order.RemainingQuantity);
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

            var filledArgs = new List<OrderFilledEventArgs>();
            book.OrderFilled += (_, e) => filledArgs.Add(e);
            TradedEventArgs tradedArgs = null;
            book.Traded += (_, e) => tradedArgs = e;

            var id1 = Guid.NewGuid();
            book.CreateLimitOrder(id1, TimeInForce.Day, Side.Buy, 100, 3);
            var now2 = new DateTime(2000, 1, 1, 12, 1, 0);
            timeProvider.SetCurrentTime(now2);
            var id2 = Guid.NewGuid();

            // act
            book.CreateLimitOrder(id2, TimeInForce.Day, Side.Sell, 100, 5);

            // assert
            Assert.IsNotNull(filledArgs);

            Assert.AreEqual(id1, filledArgs[0].Fill.Order.Id);
            Assert.AreEqual(sec, filledArgs[0].Fill.Order.Security);
            Assert.AreEqual(now1, filledArgs[0].Fill.Order.CreatedTime);
            Assert.AreEqual(now1, filledArgs[0].Fill.Order.ModifiedTime);
            Assert.AreEqual(now2, filledArgs[0].Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, filledArgs[0].Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filledArgs[0].Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filledArgs[0].Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Buy, filledArgs[0].Fill.Order.Side);
            Assert.AreEqual(100, filledArgs[0].Fill.Order.Price);
            Assert.IsNull(filledArgs[0].Fill.Order.StopPrice);
            Assert.AreEqual(3, filledArgs[0].Fill.Order.Quantity);
            Assert.AreEqual(3, filledArgs[0].Fill.Order.FilledQuantity);
            Assert.AreEqual(0, filledArgs[0].Fill.Order.RemainingQuantity);
            Assert.AreEqual(now2, filledArgs[0].Fill.Time);
            Assert.AreEqual(100, filledArgs[0].Fill.Price);
            Assert.AreEqual(3, filledArgs[0].Fill.Quantity);
            Assert.IsFalse(filledArgs[0].Fill.IsAggressor);

            Assert.AreEqual(id2, filledArgs[1].Fill.Order.Id);
            Assert.AreEqual(sec, filledArgs[1].Fill.Order.Security);
            Assert.AreEqual(now2, filledArgs[1].Fill.Order.CreatedTime);
            Assert.AreEqual(now2, filledArgs[1].Fill.Order.ModifiedTime);
            Assert.IsNull(filledArgs[1].Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, filledArgs[1].Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filledArgs[1].Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filledArgs[1].Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Sell, filledArgs[1].Fill.Order.Side);
            Assert.AreEqual(100, filledArgs[1].Fill.Order.Price);
            Assert.IsNull(filledArgs[1].Fill.Order.StopPrice);
            Assert.AreEqual(5, filledArgs[1].Fill.Order.Quantity);
            Assert.AreEqual(3, filledArgs[1].Fill.Order.FilledQuantity);
            Assert.AreEqual(2, filledArgs[1].Fill.Order.RemainingQuantity);
            Assert.AreEqual(now2, filledArgs[1].Fill.Time);
            Assert.AreEqual(100, filledArgs[1].Fill.Price);
            Assert.AreEqual(3, filledArgs[1].Fill.Quantity);
            Assert.IsTrue(filledArgs[1].Fill.IsAggressor);

            Assert.AreEqual(filledArgs[0].Fill, tradedArgs.Fills[0]);
            Assert.AreEqual(filledArgs[1].Fill, tradedArgs.Fills[1]);
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

            var filledArgs = new List<OrderFilledEventArgs>();
            book.OrderFilled += (_, e) => filledArgs.Add(e);
            TradedEventArgs tradedArgs = null;
            book.Traded += (_, e) => tradedArgs = e;

            var id1 = Guid.NewGuid();
            book.CreateLimitOrder(id1, TimeInForce.Day, Side.Buy, 110, 3);
            var now2 = new DateTime(2000, 1, 1, 12, 1, 0);
            timeProvider.SetCurrentTime(now2);
            var id2 = Guid.NewGuid();

            // act
            book.CreateLimitOrder(id2, TimeInForce.Day, Side.Sell, 100, 5);

            // assert
            Assert.IsNotNull(filledArgs);

            Assert.AreEqual(id1, filledArgs[0].Fill.Order.Id);
            Assert.AreEqual(sec, filledArgs[0].Fill.Order.Security);
            Assert.AreEqual(now1, filledArgs[0].Fill.Order.CreatedTime);
            Assert.AreEqual(now1, filledArgs[0].Fill.Order.ModifiedTime);
            Assert.AreEqual(now2, filledArgs[0].Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, filledArgs[0].Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filledArgs[0].Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filledArgs[0].Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Buy, filledArgs[0].Fill.Order.Side);
            Assert.AreEqual(110, filledArgs[0].Fill.Order.Price);
            Assert.IsNull(filledArgs[0].Fill.Order.StopPrice);
            Assert.AreEqual(3, filledArgs[0].Fill.Order.Quantity);
            Assert.AreEqual(3, filledArgs[0].Fill.Order.FilledQuantity);
            Assert.AreEqual(0, filledArgs[0].Fill.Order.RemainingQuantity);
            Assert.AreEqual(now2, filledArgs[0].Fill.Time);
            Assert.AreEqual(110, filledArgs[0].Fill.Price);
            Assert.AreEqual(3, filledArgs[0].Fill.Quantity);
            Assert.IsFalse(filledArgs[0].Fill.IsAggressor);

            Assert.AreEqual(id2, filledArgs[1].Fill.Order.Id);
            Assert.AreEqual(sec, filledArgs[1].Fill.Order.Security);
            Assert.AreEqual(now2, filledArgs[1].Fill.Order.CreatedTime);
            Assert.AreEqual(now2, filledArgs[1].Fill.Order.ModifiedTime);
            Assert.IsNull(filledArgs[1].Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, filledArgs[1].Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filledArgs[1].Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filledArgs[1].Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Sell, filledArgs[1].Fill.Order.Side);
            Assert.AreEqual(100, filledArgs[1].Fill.Order.Price);
            Assert.IsNull(filledArgs[1].Fill.Order.StopPrice);
            Assert.AreEqual(5, filledArgs[1].Fill.Order.Quantity);
            Assert.AreEqual(3, filledArgs[1].Fill.Order.FilledQuantity);
            Assert.AreEqual(2, filledArgs[1].Fill.Order.RemainingQuantity);
            Assert.AreEqual(now2, filledArgs[1].Fill.Time);
            Assert.AreEqual(110, filledArgs[1].Fill.Price);
            Assert.AreEqual(3, filledArgs[1].Fill.Quantity);
            Assert.IsTrue(filledArgs[1].Fill.IsAggressor);

            Assert.AreEqual(filledArgs[0].Fill, tradedArgs.Fills[0]);
            Assert.AreEqual(filledArgs[1].Fill, tradedArgs.Fills[1]);
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

            var filledArgs = new List<OrderFilledEventArgs>();
            book.OrderFilled += (_, e) => filledArgs.Add(e);
            TradedEventArgs tradedArgs = null;
            book.Traded += (_, e) => tradedArgs = e;

            var id1 = Guid.NewGuid();
            book.CreateLimitOrder(id1, TimeInForce.Day, Side.Buy, 110, 5);
            var now2 = new DateTime(2000, 1, 1, 12, 1, 0);
            timeProvider.SetCurrentTime(now2);
            var id2 = Guid.NewGuid();

            // act
            book.CreateLimitOrder(id2, TimeInForce.Day, Side.Sell, 100, 3);

            // assert
            Assert.IsNotNull(filledArgs);

            Assert.AreEqual(id1, filledArgs[0].Fill.Order.Id);
            Assert.AreEqual(sec, filledArgs[0].Fill.Order.Security);
            Assert.AreEqual(now1, filledArgs[0].Fill.Order.CreatedTime);
            Assert.AreEqual(now1, filledArgs[0].Fill.Order.ModifiedTime);
            Assert.IsNull(filledArgs[0].Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, filledArgs[0].Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filledArgs[0].Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filledArgs[0].Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Buy, filledArgs[0].Fill.Order.Side);
            Assert.AreEqual(110, filledArgs[0].Fill.Order.Price);
            Assert.IsNull(filledArgs[0].Fill.Order.StopPrice);
            Assert.AreEqual(5, filledArgs[0].Fill.Order.Quantity);
            Assert.AreEqual(3, filledArgs[0].Fill.Order.FilledQuantity);
            Assert.AreEqual(2, filledArgs[0].Fill.Order.RemainingQuantity);
            Assert.AreEqual(now2, filledArgs[0].Fill.Time);
            Assert.AreEqual(110, filledArgs[0].Fill.Price);
            Assert.AreEqual(3, filledArgs[0].Fill.Quantity);
            Assert.IsFalse(filledArgs[0].Fill.IsAggressor);

            Assert.AreEqual(id2, filledArgs[1].Fill.Order.Id);
            Assert.AreEqual(sec, filledArgs[1].Fill.Order.Security);
            Assert.AreEqual(now2, filledArgs[1].Fill.Order.CreatedTime);
            Assert.AreEqual(now2, filledArgs[1].Fill.Order.ModifiedTime);
            Assert.AreEqual(now2, filledArgs[1].Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, filledArgs[1].Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filledArgs[1].Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filledArgs[1].Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Sell, filledArgs[1].Fill.Order.Side);
            Assert.AreEqual(100, filledArgs[1].Fill.Order.Price);
            Assert.IsNull(filledArgs[1].Fill.Order.StopPrice);
            Assert.AreEqual(3, filledArgs[1].Fill.Order.Quantity);
            Assert.AreEqual(3, filledArgs[1].Fill.Order.FilledQuantity);
            Assert.AreEqual(0, filledArgs[1].Fill.Order.RemainingQuantity);
            Assert.AreEqual(now2, filledArgs[1].Fill.Time);
            Assert.AreEqual(110, filledArgs[1].Fill.Price);
            Assert.AreEqual(3, filledArgs[1].Fill.Quantity);
            Assert.IsTrue(filledArgs[1].Fill.IsAggressor);

            Assert.AreEqual(filledArgs[0].Fill, tradedArgs.Fills[0]);
            Assert.AreEqual(filledArgs[1].Fill, tradedArgs.Fills[1]);
        }
        
        
        [Test]
        public void CreateLimitOrder_MatchAgainstOrderByTime()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now1 = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now1);
            var book = new OrderBook.OrderBook(sec, timeProvider);
            book.SetStatus(OrderBookStatus.Open);

            var filledArgs = new List<OrderFilledEventArgs>();
            book.OrderFilled += (_, e) => filledArgs.Add(e);
            TradedEventArgs tradedArgs = null;
            book.Traded += (_, e) => tradedArgs = e;

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
            book.CreateLimitOrder(id3, TimeInForce.Day, Side.Sell, 100, 3);

            // assert
            Assert.IsNotNull(filledArgs);

            Assert.AreEqual(id2, filledArgs[0].Fill.Order.Id);
            Assert.AreEqual(sec, filledArgs[0].Fill.Order.Security);
            Assert.AreEqual(now2, filledArgs[0].Fill.Order.CreatedTime);
            Assert.AreEqual(now2, filledArgs[0].Fill.Order.ModifiedTime);
            Assert.IsNull(filledArgs[0].Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, filledArgs[0].Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filledArgs[0].Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filledArgs[0].Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Buy, filledArgs[0].Fill.Order.Side);
            Assert.AreEqual(120, filledArgs[0].Fill.Order.Price);
            Assert.IsNull(filledArgs[0].Fill.Order.StopPrice);
            Assert.AreEqual(5, filledArgs[0].Fill.Order.Quantity);
            Assert.AreEqual(3, filledArgs[0].Fill.Order.FilledQuantity);
            Assert.AreEqual(2, filledArgs[0].Fill.Order.RemainingQuantity);
            Assert.AreEqual(now3, filledArgs[0].Fill.Time);
            Assert.AreEqual(120, filledArgs[0].Fill.Price);
            Assert.AreEqual(3, filledArgs[0].Fill.Quantity);
            Assert.IsFalse(filledArgs[0].Fill.IsAggressor);

            Assert.AreEqual(id3, filledArgs[1].Fill.Order.Id);
            Assert.AreEqual(sec, filledArgs[1].Fill.Order.Security);
            Assert.AreEqual(now3, filledArgs[1].Fill.Order.CreatedTime);
            Assert.AreEqual(now3, filledArgs[1].Fill.Order.ModifiedTime);
            Assert.AreEqual(now3, filledArgs[1].Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, filledArgs[1].Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filledArgs[1].Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filledArgs[1].Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Sell, filledArgs[1].Fill.Order.Side);
            Assert.AreEqual(100, filledArgs[1].Fill.Order.Price);
            Assert.IsNull(filledArgs[1].Fill.Order.StopPrice);
            Assert.AreEqual(3, filledArgs[1].Fill.Order.Quantity);
            Assert.AreEqual(3, filledArgs[1].Fill.Order.FilledQuantity);
            Assert.AreEqual(0, filledArgs[1].Fill.Order.RemainingQuantity);
            Assert.AreEqual(now3, filledArgs[1].Fill.Time);
            Assert.AreEqual(120, filledArgs[1].Fill.Price);
            Assert.AreEqual(3, filledArgs[1].Fill.Quantity);
            Assert.IsTrue(filledArgs[1].Fill.IsAggressor);

            Assert.AreEqual(filledArgs[0].Fill, tradedArgs.Fills[0]);
            Assert.AreEqual(filledArgs[1].Fill, tradedArgs.Fills[1]);
        }
        
        [Test]
        public void CreateLimitOrder_MatchAgainstOrdersByPrice()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now1 = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now1);
            var book = new OrderBook.OrderBook(sec, timeProvider);
            book.SetStatus(OrderBookStatus.Open);

            var filledArgs = new List<OrderFilledEventArgs>();
            book.OrderFilled += (_, e) => filledArgs.Add(e);
            TradedEventArgs tradedArgs = null;
            book.Traded += (_, e) => tradedArgs = e;

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
            book.CreateLimitOrder(id3, TimeInForce.Day, Side.Sell, 100, 8);

            // assert
            Assert.IsNotNull(filledArgs);

            Assert.AreEqual(id2, filledArgs[0].Fill.Order.Id);
            Assert.AreEqual(sec, filledArgs[0].Fill.Order.Security);
            Assert.AreEqual(now2, filledArgs[0].Fill.Order.CreatedTime);
            Assert.AreEqual(now2, filledArgs[0].Fill.Order.ModifiedTime);
            Assert.AreEqual(now3, filledArgs[0].Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, filledArgs[0].Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filledArgs[0].Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filledArgs[0].Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Buy, filledArgs[0].Fill.Order.Side);
            Assert.AreEqual(120, filledArgs[0].Fill.Order.Price);
            Assert.IsNull(filledArgs[0].Fill.Order.StopPrice);
            Assert.AreEqual(5, filledArgs[0].Fill.Order.Quantity);
            Assert.AreEqual(5, filledArgs[0].Fill.Order.FilledQuantity);
            Assert.AreEqual(0, filledArgs[0].Fill.Order.RemainingQuantity);
            Assert.AreEqual(now3, filledArgs[0].Fill.Time);
            Assert.AreEqual(120, filledArgs[0].Fill.Price);
            Assert.AreEqual(5, filledArgs[0].Fill.Quantity);
            Assert.IsFalse(filledArgs[0].Fill.IsAggressor);

            Assert.AreEqual(id3, filledArgs[1].Fill.Order.Id);
            Assert.AreEqual(sec, filledArgs[1].Fill.Order.Security);
            Assert.AreEqual(now3, filledArgs[1].Fill.Order.CreatedTime);
            Assert.AreEqual(now3, filledArgs[1].Fill.Order.ModifiedTime);
            Assert.IsNull(filledArgs[1].Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, filledArgs[1].Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filledArgs[1].Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filledArgs[1].Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Sell, filledArgs[1].Fill.Order.Side);
            Assert.AreEqual(100, filledArgs[1].Fill.Order.Price);
            Assert.IsNull(filledArgs[1].Fill.Order.StopPrice);
            Assert.AreEqual(8, filledArgs[1].Fill.Order.Quantity);
            Assert.AreEqual(5, filledArgs[1].Fill.Order.FilledQuantity);
            Assert.AreEqual(3, filledArgs[1].Fill.Order.RemainingQuantity);
            Assert.AreEqual(now3, filledArgs[1].Fill.Time);
            Assert.AreEqual(120, filledArgs[1].Fill.Price);
            Assert.AreEqual(5, filledArgs[1].Fill.Quantity);
            Assert.IsTrue(filledArgs[1].Fill.IsAggressor);
            
            Assert.AreEqual(id1, filledArgs[2].Fill.Order.Id);
            Assert.AreEqual(sec, filledArgs[2].Fill.Order.Security);
            Assert.AreEqual(now1, filledArgs[2].Fill.Order.CreatedTime);
            Assert.AreEqual(now1, filledArgs[2].Fill.Order.ModifiedTime);
            Assert.IsNull(filledArgs[2].Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, filledArgs[2].Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filledArgs[2].Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filledArgs[2].Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Buy, filledArgs[2].Fill.Order.Side);
            Assert.AreEqual(110, filledArgs[2].Fill.Order.Price);
            Assert.IsNull(filledArgs[2].Fill.Order.StopPrice);
            Assert.AreEqual(5, filledArgs[2].Fill.Order.Quantity);
            Assert.AreEqual(3, filledArgs[2].Fill.Order.FilledQuantity);
            Assert.AreEqual(2, filledArgs[2].Fill.Order.RemainingQuantity);
            Assert.AreEqual(now3, filledArgs[2].Fill.Time);
            Assert.AreEqual(110, filledArgs[2].Fill.Price);
            Assert.AreEqual(3, filledArgs[2].Fill.Quantity);
            Assert.IsFalse(filledArgs[2].Fill.IsAggressor);
            
            Assert.AreEqual(id3, filledArgs[3].Fill.Order.Id);
            Assert.AreEqual(sec, filledArgs[3].Fill.Order.Security);
            Assert.AreEqual(now3, filledArgs[3].Fill.Order.CreatedTime);
            Assert.AreEqual(now3, filledArgs[3].Fill.Order.ModifiedTime);
            Assert.AreEqual(now3, filledArgs[3].Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, filledArgs[3].Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filledArgs[3].Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filledArgs[3].Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Sell, filledArgs[3].Fill.Order.Side);
            Assert.AreEqual(100, filledArgs[3].Fill.Order.Price);
            Assert.IsNull(filledArgs[3].Fill.Order.StopPrice);
            Assert.AreEqual(8, filledArgs[3].Fill.Order.Quantity);
            Assert.AreEqual(8, filledArgs[3].Fill.Order.FilledQuantity);
            Assert.AreEqual(0, filledArgs[3].Fill.Order.RemainingQuantity);
            Assert.AreEqual(now3, filledArgs[3].Fill.Time);
            Assert.AreEqual(110, filledArgs[3].Fill.Price);
            Assert.AreEqual(3, filledArgs[3].Fill.Quantity);
            Assert.IsTrue(filledArgs[3].Fill.IsAggressor);

            Assert.AreEqual(filledArgs[0].Fill, tradedArgs.Fills[0]);
            Assert.AreEqual(filledArgs[1].Fill, tradedArgs.Fills[1]);
            Assert.AreEqual(filledArgs[2].Fill, tradedArgs.Fills[2]);
            Assert.AreEqual(filledArgs[3].Fill, tradedArgs.Fills[3]);
        }

        [Test]
        public void CreateLimitOrder_MatchAgainstOrderAtSamePriceByTime()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now1 = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now1);
            var book = new OrderBook.OrderBook(sec, timeProvider);
            book.SetStatus(OrderBookStatus.Open);

            var filledArgs = new List<OrderFilledEventArgs>();
            book.OrderFilled += (_, e) => filledArgs.Add(e);
            TradedEventArgs tradedArgs = null;
            book.Traded += (_, e) => tradedArgs = e;

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
            book.CreateLimitOrder(id3, TimeInForce.Day, Side.Sell, 100, 3);

            // assert
            Assert.IsNotNull(filledArgs);

            Assert.AreEqual(id1, filledArgs[0].Fill.Order.Id);
            Assert.AreEqual(sec, filledArgs[0].Fill.Order.Security);
            Assert.AreEqual(now1, filledArgs[0].Fill.Order.CreatedTime);
            Assert.AreEqual(now1, filledArgs[0].Fill.Order.ModifiedTime);
            Assert.IsNull(filledArgs[0].Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, filledArgs[0].Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filledArgs[0].Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filledArgs[0].Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Buy, filledArgs[0].Fill.Order.Side);
            Assert.AreEqual(110, filledArgs[0].Fill.Order.Price);
            Assert.IsNull(filledArgs[0].Fill.Order.StopPrice);
            Assert.AreEqual(5, filledArgs[0].Fill.Order.Quantity);
            Assert.AreEqual(3, filledArgs[0].Fill.Order.FilledQuantity);
            Assert.AreEqual(2, filledArgs[0].Fill.Order.RemainingQuantity);
            Assert.AreEqual(now3, filledArgs[0].Fill.Time);
            Assert.AreEqual(110, filledArgs[0].Fill.Price);
            Assert.AreEqual(3, filledArgs[0].Fill.Quantity);
            Assert.IsFalse(filledArgs[0].Fill.IsAggressor);

            Assert.AreEqual(id3, filledArgs[1].Fill.Order.Id);
            Assert.AreEqual(sec, filledArgs[1].Fill.Order.Security);
            Assert.AreEqual(now3, filledArgs[1].Fill.Order.CreatedTime);
            Assert.AreEqual(now3, filledArgs[1].Fill.Order.ModifiedTime);
            Assert.AreEqual(now3, filledArgs[1].Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, filledArgs[1].Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filledArgs[1].Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filledArgs[1].Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Sell, filledArgs[1].Fill.Order.Side);
            Assert.AreEqual(100, filledArgs[1].Fill.Order.Price);
            Assert.IsNull(filledArgs[1].Fill.Order.StopPrice);
            Assert.AreEqual(3, filledArgs[1].Fill.Order.Quantity);
            Assert.AreEqual(3, filledArgs[1].Fill.Order.FilledQuantity);
            Assert.AreEqual(0, filledArgs[1].Fill.Order.RemainingQuantity);
            Assert.AreEqual(now3, filledArgs[1].Fill.Time);
            Assert.AreEqual(110, filledArgs[1].Fill.Price);
            Assert.AreEqual(3, filledArgs[1].Fill.Quantity);
            Assert.IsTrue(filledArgs[1].Fill.IsAggressor);

            Assert.AreEqual(filledArgs[0].Fill, tradedArgs.Fills[0]);
            Assert.AreEqual(filledArgs[1].Fill, tradedArgs.Fills[1]);
        }
        
        [Test]
        public void CreateLimitOrder_MatchAgainstOrdersAtSamePriceByTime()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now1 = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now1);
            var book = new OrderBook.OrderBook(sec, timeProvider);
            book.SetStatus(OrderBookStatus.Open);

            var filledArgs = new List<OrderFilledEventArgs>();
            book.OrderFilled += (_, e) => filledArgs.Add(e);
            TradedEventArgs tradedArgs = null;
            book.Traded += (_, e) => tradedArgs = e;

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
            book.CreateLimitOrder(id3, TimeInForce.Day, Side.Sell, 100, 8);

            // assert
            Assert.IsNotNull(filledArgs);

            Assert.AreEqual(id1, filledArgs[0].Fill.Order.Id);
            Assert.AreEqual(sec, filledArgs[0].Fill.Order.Security);
            Assert.AreEqual(now1, filledArgs[0].Fill.Order.CreatedTime);
            Assert.AreEqual(now1, filledArgs[0].Fill.Order.ModifiedTime);
            Assert.AreEqual(now3, filledArgs[0].Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, filledArgs[0].Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filledArgs[0].Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filledArgs[0].Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Buy, filledArgs[0].Fill.Order.Side);
            Assert.AreEqual(110, filledArgs[0].Fill.Order.Price);
            Assert.IsNull(filledArgs[0].Fill.Order.StopPrice);
            Assert.AreEqual(5, filledArgs[0].Fill.Order.Quantity);
            Assert.AreEqual(5, filledArgs[0].Fill.Order.FilledQuantity);
            Assert.AreEqual(0, filledArgs[0].Fill.Order.RemainingQuantity);
            Assert.AreEqual(now3, filledArgs[0].Fill.Time);
            Assert.AreEqual(110, filledArgs[0].Fill.Price);
            Assert.AreEqual(5, filledArgs[0].Fill.Quantity);
            Assert.IsFalse(filledArgs[0].Fill.IsAggressor);

            Assert.AreEqual(id3, filledArgs[1].Fill.Order.Id);
            Assert.AreEqual(sec, filledArgs[1].Fill.Order.Security);
            Assert.AreEqual(now3, filledArgs[1].Fill.Order.CreatedTime);
            Assert.AreEqual(now3, filledArgs[1].Fill.Order.ModifiedTime);
            Assert.IsNull(filledArgs[1].Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, filledArgs[1].Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filledArgs[1].Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filledArgs[1].Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Sell, filledArgs[1].Fill.Order.Side);
            Assert.AreEqual(100, filledArgs[1].Fill.Order.Price);
            Assert.IsNull(filledArgs[1].Fill.Order.StopPrice);
            Assert.AreEqual(8, filledArgs[1].Fill.Order.Quantity);
            Assert.AreEqual(5, filledArgs[1].Fill.Order.FilledQuantity);
            Assert.AreEqual(3, filledArgs[1].Fill.Order.RemainingQuantity);
            Assert.AreEqual(now3, filledArgs[1].Fill.Time);
            Assert.AreEqual(110, filledArgs[1].Fill.Price);
            Assert.AreEqual(5, filledArgs[1].Fill.Quantity);
            Assert.IsTrue(filledArgs[1].Fill.IsAggressor);
            
            Assert.AreEqual(id2, filledArgs[2].Fill.Order.Id);
            Assert.AreEqual(sec, filledArgs[2].Fill.Order.Security);
            Assert.AreEqual(now2, filledArgs[2].Fill.Order.CreatedTime);
            Assert.AreEqual(now2, filledArgs[2].Fill.Order.ModifiedTime);
            Assert.IsNull(filledArgs[2].Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, filledArgs[2].Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filledArgs[2].Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filledArgs[2].Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Buy, filledArgs[2].Fill.Order.Side);
            Assert.AreEqual(110, filledArgs[2].Fill.Order.Price);
            Assert.IsNull(filledArgs[2].Fill.Order.StopPrice);
            Assert.AreEqual(5, filledArgs[2].Fill.Order.Quantity);
            Assert.AreEqual(3, filledArgs[2].Fill.Order.FilledQuantity);
            Assert.AreEqual(2, filledArgs[2].Fill.Order.RemainingQuantity);
            Assert.AreEqual(now3, filledArgs[2].Fill.Time);
            Assert.AreEqual(110, filledArgs[2].Fill.Price);
            Assert.AreEqual(3, filledArgs[2].Fill.Quantity);
            Assert.IsFalse(filledArgs[2].Fill.IsAggressor);
            
            Assert.AreEqual(id3, filledArgs[3].Fill.Order.Id);
            Assert.AreEqual(sec, filledArgs[3].Fill.Order.Security);
            Assert.AreEqual(now3, filledArgs[3].Fill.Order.CreatedTime);
            Assert.AreEqual(now3, filledArgs[3].Fill.Order.ModifiedTime);
            Assert.AreEqual(now3, filledArgs[3].Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, filledArgs[3].Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filledArgs[3].Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filledArgs[3].Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Sell, filledArgs[3].Fill.Order.Side);
            Assert.AreEqual(100, filledArgs[3].Fill.Order.Price);
            Assert.IsNull(filledArgs[3].Fill.Order.StopPrice);
            Assert.AreEqual(8, filledArgs[3].Fill.Order.Quantity);
            Assert.AreEqual(8, filledArgs[3].Fill.Order.FilledQuantity);
            Assert.AreEqual(0, filledArgs[3].Fill.Order.RemainingQuantity);
            Assert.AreEqual(now3, filledArgs[3].Fill.Time);
            Assert.AreEqual(110, filledArgs[3].Fill.Price);
            Assert.AreEqual(3, filledArgs[3].Fill.Quantity);
            Assert.IsTrue(filledArgs[3].Fill.IsAggressor);

            Assert.AreEqual(filledArgs[0].Fill, tradedArgs.Fills[0]);
            Assert.AreEqual(filledArgs[1].Fill, tradedArgs.Fills[1]);
            Assert.AreEqual(filledArgs[2].Fill, tradedArgs.Fills[2]);
            Assert.AreEqual(filledArgs[3].Fill, tradedArgs.Fills[3]);
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

            var filledArgs = new List<OrderFilledEventArgs>();
            book.OrderFilled += (_, e) => filledArgs.Add(e);
            TradedEventArgs tradedArgs = null;
            book.Traded += (_, e) => tradedArgs = e;

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
            book.CreateLimitOrder(id3, TimeInForce.Day, Side.Sell, 100, 3);

            // assert
            Assert.IsNotNull(filledArgs);

            Assert.AreEqual(id2, filledArgs[0].Fill.Order.Id);
            Assert.AreEqual(sec, filledArgs[0].Fill.Order.Security);
            Assert.AreEqual(now2, filledArgs[0].Fill.Order.CreatedTime);
            Assert.AreEqual(now2, filledArgs[0].Fill.Order.ModifiedTime);
            Assert.IsNull(filledArgs[0].Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, filledArgs[0].Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filledArgs[0].Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filledArgs[0].Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Buy, filledArgs[0].Fill.Order.Side);
            Assert.AreEqual(110, filledArgs[0].Fill.Order.Price);
            Assert.IsNull(filledArgs[0].Fill.Order.StopPrice);
            Assert.AreEqual(5, filledArgs[0].Fill.Order.Quantity);
            Assert.AreEqual(3, filledArgs[0].Fill.Order.FilledQuantity);
            Assert.AreEqual(2, filledArgs[0].Fill.Order.RemainingQuantity);
            Assert.AreEqual(now4, filledArgs[0].Fill.Time);
            Assert.AreEqual(110, filledArgs[0].Fill.Price);
            Assert.AreEqual(3, filledArgs[0].Fill.Quantity);
            Assert.IsFalse(filledArgs[0].Fill.IsAggressor);

            Assert.AreEqual(id3, filledArgs[1].Fill.Order.Id);
            Assert.AreEqual(sec, filledArgs[1].Fill.Order.Security);
            Assert.AreEqual(now4, filledArgs[1].Fill.Order.CreatedTime);
            Assert.AreEqual(now4, filledArgs[1].Fill.Order.ModifiedTime);
            Assert.AreEqual(now4, filledArgs[1].Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, filledArgs[1].Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filledArgs[1].Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filledArgs[1].Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Sell, filledArgs[1].Fill.Order.Side);
            Assert.AreEqual(100, filledArgs[1].Fill.Order.Price);
            Assert.IsNull(filledArgs[1].Fill.Order.StopPrice);
            Assert.AreEqual(3, filledArgs[1].Fill.Order.Quantity);
            Assert.AreEqual(3, filledArgs[1].Fill.Order.FilledQuantity);
            Assert.AreEqual(0, filledArgs[1].Fill.Order.RemainingQuantity);
            Assert.AreEqual(now4, filledArgs[1].Fill.Time);
            Assert.AreEqual(110, filledArgs[1].Fill.Price);
            Assert.AreEqual(3, filledArgs[1].Fill.Quantity);
            Assert.IsTrue(filledArgs[1].Fill.IsAggressor);

            Assert.AreEqual(filledArgs[0].Fill, tradedArgs.Fills[0]);
            Assert.AreEqual(filledArgs[1].Fill, tradedArgs.Fills[1]);
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

            var filledArgs = new List<OrderFilledEventArgs>();
            book.OrderFilled += (_, e) => filledArgs.Add(e);
            TradedEventArgs tradedArgs = null;
            book.Traded += (_, e) => tradedArgs = e;

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
            book.CreateLimitOrder(id3, TimeInForce.Day, Side.Sell, 100, 8);

            // assert
            Assert.IsNotNull(filledArgs);

            Assert.AreEqual(id2, filledArgs[0].Fill.Order.Id);
            Assert.AreEqual(sec, filledArgs[0].Fill.Order.Security);
            Assert.AreEqual(now2, filledArgs[0].Fill.Order.CreatedTime);
            Assert.AreEqual(now2, filledArgs[0].Fill.Order.ModifiedTime);
            Assert.AreEqual(now4, filledArgs[0].Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, filledArgs[0].Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filledArgs[0].Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filledArgs[0].Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Buy, filledArgs[0].Fill.Order.Side);
            Assert.AreEqual(110, filledArgs[0].Fill.Order.Price);
            Assert.IsNull(filledArgs[0].Fill.Order.StopPrice);
            Assert.AreEqual(5, filledArgs[0].Fill.Order.Quantity);
            Assert.AreEqual(5, filledArgs[0].Fill.Order.FilledQuantity);
            Assert.AreEqual(0, filledArgs[0].Fill.Order.RemainingQuantity);
            Assert.AreEqual(now4, filledArgs[0].Fill.Time);
            Assert.AreEqual(110, filledArgs[0].Fill.Price);
            Assert.AreEqual(5, filledArgs[0].Fill.Quantity);
            Assert.IsFalse(filledArgs[0].Fill.IsAggressor);

            Assert.AreEqual(id3, filledArgs[1].Fill.Order.Id);
            Assert.AreEqual(sec, filledArgs[1].Fill.Order.Security);
            Assert.AreEqual(now4, filledArgs[1].Fill.Order.CreatedTime);
            Assert.AreEqual(now4, filledArgs[1].Fill.Order.ModifiedTime);
            Assert.IsNull(filledArgs[1].Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, filledArgs[1].Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filledArgs[1].Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filledArgs[1].Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Sell, filledArgs[1].Fill.Order.Side);
            Assert.AreEqual(100, filledArgs[1].Fill.Order.Price);
            Assert.IsNull(filledArgs[1].Fill.Order.StopPrice);
            Assert.AreEqual(8, filledArgs[1].Fill.Order.Quantity);
            Assert.AreEqual(5, filledArgs[1].Fill.Order.FilledQuantity);
            Assert.AreEqual(3, filledArgs[1].Fill.Order.RemainingQuantity);
            Assert.AreEqual(now4, filledArgs[1].Fill.Time);
            Assert.AreEqual(110, filledArgs[1].Fill.Price);
            Assert.AreEqual(5, filledArgs[1].Fill.Quantity);
            Assert.IsTrue(filledArgs[1].Fill.IsAggressor);
            
            Assert.AreEqual(id1, filledArgs[2].Fill.Order.Id);
            Assert.AreEqual(sec, filledArgs[2].Fill.Order.Security);
            Assert.AreEqual(now1, filledArgs[2].Fill.Order.CreatedTime);
            Assert.AreEqual(now3, filledArgs[2].Fill.Order.ModifiedTime);
            Assert.IsNull(filledArgs[2].Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, filledArgs[2].Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filledArgs[2].Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filledArgs[2].Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Buy, filledArgs[2].Fill.Order.Side);
            Assert.AreEqual(110, filledArgs[2].Fill.Order.Price);
            Assert.IsNull(filledArgs[2].Fill.Order.StopPrice);
            Assert.AreEqual(6, filledArgs[2].Fill.Order.Quantity);
            Assert.AreEqual(3, filledArgs[2].Fill.Order.FilledQuantity);
            Assert.AreEqual(3, filledArgs[2].Fill.Order.RemainingQuantity);
            Assert.AreEqual(now4, filledArgs[2].Fill.Time);
            Assert.AreEqual(110, filledArgs[2].Fill.Price);
            Assert.AreEqual(3, filledArgs[2].Fill.Quantity);
            Assert.IsFalse(filledArgs[2].Fill.IsAggressor);
            
            Assert.AreEqual(id3, filledArgs[3].Fill.Order.Id);
            Assert.AreEqual(sec, filledArgs[3].Fill.Order.Security);
            Assert.AreEqual(now4, filledArgs[3].Fill.Order.CreatedTime);
            Assert.AreEqual(now4, filledArgs[3].Fill.Order.ModifiedTime);
            Assert.AreEqual(now4, filledArgs[3].Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, filledArgs[3].Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filledArgs[3].Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filledArgs[3].Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Sell, filledArgs[3].Fill.Order.Side);
            Assert.AreEqual(100, filledArgs[3].Fill.Order.Price);
            Assert.IsNull(filledArgs[3].Fill.Order.StopPrice);
            Assert.AreEqual(8, filledArgs[3].Fill.Order.Quantity);
            Assert.AreEqual(8, filledArgs[3].Fill.Order.FilledQuantity);
            Assert.AreEqual(0, filledArgs[3].Fill.Order.RemainingQuantity);
            Assert.AreEqual(now4, filledArgs[3].Fill.Time);
            Assert.AreEqual(110, filledArgs[3].Fill.Price);
            Assert.AreEqual(3, filledArgs[3].Fill.Quantity);
            Assert.IsTrue(filledArgs[3].Fill.IsAggressor);

            Assert.AreEqual(filledArgs[0].Fill, tradedArgs.Fills[0]);
            Assert.AreEqual(filledArgs[1].Fill, tradedArgs.Fills[1]);
            Assert.AreEqual(filledArgs[2].Fill, tradedArgs.Fills[2]);
            Assert.AreEqual(filledArgs[3].Fill, tradedArgs.Fills[3]);
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

            var filledArgs = new List<OrderFilledEventArgs>();
            book.OrderFilled += (_, e) => filledArgs.Add(e);
            TradedEventArgs tradedArgs = null;
            book.Traded += (_, e) => tradedArgs = e;

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
            book.CreateLimitOrder(id3, TimeInForce.Day, Side.Sell, 100, 3);

            // assert
            Assert.IsNotNull(filledArgs);

            Assert.AreEqual(id1, filledArgs[0].Fill.Order.Id);
            Assert.AreEqual(sec, filledArgs[0].Fill.Order.Security);
            Assert.AreEqual(now1, filledArgs[0].Fill.Order.CreatedTime);
            Assert.AreEqual(now3, filledArgs[0].Fill.Order.ModifiedTime);
            Assert.IsNull(filledArgs[0].Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, filledArgs[0].Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filledArgs[0].Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filledArgs[0].Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Buy, filledArgs[0].Fill.Order.Side);
            Assert.AreEqual(110, filledArgs[0].Fill.Order.Price);
            Assert.IsNull(filledArgs[0].Fill.Order.StopPrice);
            Assert.AreEqual(4, filledArgs[0].Fill.Order.Quantity);
            Assert.AreEqual(3, filledArgs[0].Fill.Order.FilledQuantity);
            Assert.AreEqual(1, filledArgs[0].Fill.Order.RemainingQuantity);
            Assert.AreEqual(now4, filledArgs[0].Fill.Time);
            Assert.AreEqual(110, filledArgs[0].Fill.Price);
            Assert.AreEqual(3, filledArgs[0].Fill.Quantity);
            Assert.IsFalse(filledArgs[0].Fill.IsAggressor);

            Assert.AreEqual(id3, filledArgs[1].Fill.Order.Id);
            Assert.AreEqual(sec, filledArgs[1].Fill.Order.Security);
            Assert.AreEqual(now4, filledArgs[1].Fill.Order.CreatedTime);
            Assert.AreEqual(now4, filledArgs[1].Fill.Order.ModifiedTime);
            Assert.AreEqual(now4, filledArgs[1].Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, filledArgs[1].Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filledArgs[1].Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filledArgs[1].Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Sell, filledArgs[1].Fill.Order.Side);
            Assert.AreEqual(100, filledArgs[1].Fill.Order.Price);
            Assert.IsNull(filledArgs[1].Fill.Order.StopPrice);
            Assert.AreEqual(3, filledArgs[1].Fill.Order.Quantity);
            Assert.AreEqual(3, filledArgs[1].Fill.Order.FilledQuantity);
            Assert.AreEqual(0, filledArgs[1].Fill.Order.RemainingQuantity);
            Assert.AreEqual(now4, filledArgs[1].Fill.Time);
            Assert.AreEqual(110, filledArgs[1].Fill.Price);
            Assert.AreEqual(3, filledArgs[1].Fill.Quantity);
            Assert.IsTrue(filledArgs[1].Fill.IsAggressor);

            Assert.AreEqual(filledArgs[0].Fill, tradedArgs.Fills[0]);
            Assert.AreEqual(filledArgs[1].Fill, tradedArgs.Fills[1]);
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

            var filledArgs = new List<OrderFilledEventArgs>();
            book.OrderFilled += (_, e) => filledArgs.Add(e);
            TradedEventArgs tradedArgs = null;
            book.Traded += (_, e) => tradedArgs = e;

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
            book.CreateLimitOrder(id3, TimeInForce.Day, Side.Sell, 100, 8);

            // assert
            Assert.IsNotNull(filledArgs);

            Assert.AreEqual(id1, filledArgs[0].Fill.Order.Id);
            Assert.AreEqual(sec, filledArgs[0].Fill.Order.Security);
            Assert.AreEqual(now1, filledArgs[0].Fill.Order.CreatedTime);
            Assert.AreEqual(now3, filledArgs[0].Fill.Order.ModifiedTime);
            Assert.AreEqual(now4, filledArgs[0].Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, filledArgs[0].Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filledArgs[0].Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filledArgs[0].Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Buy, filledArgs[0].Fill.Order.Side);
            Assert.AreEqual(110, filledArgs[0].Fill.Order.Price);
            Assert.IsNull(filledArgs[0].Fill.Order.StopPrice);
            Assert.AreEqual(4, filledArgs[0].Fill.Order.Quantity);
            Assert.AreEqual(4, filledArgs[0].Fill.Order.FilledQuantity);
            Assert.AreEqual(0, filledArgs[0].Fill.Order.RemainingQuantity);
            Assert.AreEqual(now4, filledArgs[0].Fill.Time);
            Assert.AreEqual(110, filledArgs[0].Fill.Price);
            Assert.AreEqual(4, filledArgs[0].Fill.Quantity);
            Assert.IsFalse(filledArgs[0].Fill.IsAggressor);

            Assert.AreEqual(id3, filledArgs[1].Fill.Order.Id);
            Assert.AreEqual(sec, filledArgs[1].Fill.Order.Security);
            Assert.AreEqual(now4, filledArgs[1].Fill.Order.CreatedTime);
            Assert.AreEqual(now4, filledArgs[1].Fill.Order.ModifiedTime);
            Assert.IsNull(filledArgs[1].Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, filledArgs[1].Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filledArgs[1].Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filledArgs[1].Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Sell, filledArgs[1].Fill.Order.Side);
            Assert.AreEqual(100, filledArgs[1].Fill.Order.Price);
            Assert.IsNull(filledArgs[1].Fill.Order.StopPrice);
            Assert.AreEqual(8, filledArgs[1].Fill.Order.Quantity);
            Assert.AreEqual(4, filledArgs[1].Fill.Order.FilledQuantity);
            Assert.AreEqual(4, filledArgs[1].Fill.Order.RemainingQuantity);
            Assert.AreEqual(now4, filledArgs[1].Fill.Time);
            Assert.AreEqual(110, filledArgs[1].Fill.Price);
            Assert.AreEqual(4, filledArgs[1].Fill.Quantity);
            Assert.IsTrue(filledArgs[1].Fill.IsAggressor);
            
            Assert.AreEqual(id2, filledArgs[2].Fill.Order.Id);
            Assert.AreEqual(sec, filledArgs[2].Fill.Order.Security);
            Assert.AreEqual(now2, filledArgs[2].Fill.Order.CreatedTime);
            Assert.AreEqual(now2, filledArgs[2].Fill.Order.ModifiedTime);
            Assert.IsNull(filledArgs[2].Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, filledArgs[2].Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filledArgs[2].Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filledArgs[2].Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Buy, filledArgs[2].Fill.Order.Side);
            Assert.AreEqual(110, filledArgs[2].Fill.Order.Price);
            Assert.IsNull(filledArgs[2].Fill.Order.StopPrice);
            Assert.AreEqual(5, filledArgs[2].Fill.Order.Quantity);
            Assert.AreEqual(4, filledArgs[2].Fill.Order.FilledQuantity);
            Assert.AreEqual(1, filledArgs[2].Fill.Order.RemainingQuantity);
            Assert.AreEqual(now4, filledArgs[2].Fill.Time);
            Assert.AreEqual(110, filledArgs[2].Fill.Price);
            Assert.AreEqual(4, filledArgs[2].Fill.Quantity);
            Assert.IsFalse(filledArgs[2].Fill.IsAggressor);
            
            Assert.AreEqual(id3, filledArgs[3].Fill.Order.Id);
            Assert.AreEqual(sec, filledArgs[3].Fill.Order.Security);
            Assert.AreEqual(now4, filledArgs[3].Fill.Order.CreatedTime);
            Assert.AreEqual(now4, filledArgs[3].Fill.Order.ModifiedTime);
            Assert.AreEqual(now4, filledArgs[3].Fill.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Filled, filledArgs[3].Fill.Order.Status);
            Assert.AreEqual(OrderType.Limit, filledArgs[3].Fill.Order.Type);
            Assert.AreEqual(TimeInForce.Day, filledArgs[3].Fill.Order.TimeInForce);
            Assert.AreEqual(Side.Sell, filledArgs[3].Fill.Order.Side);
            Assert.AreEqual(100, filledArgs[3].Fill.Order.Price);
            Assert.IsNull(filledArgs[3].Fill.Order.StopPrice);
            Assert.AreEqual(8, filledArgs[3].Fill.Order.Quantity);
            Assert.AreEqual(8, filledArgs[3].Fill.Order.FilledQuantity);
            Assert.AreEqual(0, filledArgs[3].Fill.Order.RemainingQuantity);
            Assert.AreEqual(now4, filledArgs[3].Fill.Time);
            Assert.AreEqual(110, filledArgs[3].Fill.Price);
            Assert.AreEqual(4, filledArgs[3].Fill.Quantity);
            Assert.IsTrue(filledArgs[3].Fill.IsAggressor);

            Assert.AreEqual(filledArgs[0].Fill, tradedArgs.Fills[0]);
            Assert.AreEqual(filledArgs[1].Fill, tradedArgs.Fills[1]);
            Assert.AreEqual(filledArgs[2].Fill, tradedArgs.Fills[2]);
            Assert.AreEqual(filledArgs[3].Fill, tradedArgs.Fills[3]);
        }
        

        [Test]
        public void CreateLimitOrder_MarketClosed_Rejected()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now);
            var book = new OrderBook.OrderBook(sec, timeProvider);

            OrderCreateRejectedEventArgs rejectedArgs = null;
            book.OrderCreateRejected += (sender, e) => { rejectedArgs = e; };
            book.SetStatus(OrderBookStatus.Closed);
            var id = Guid.NewGuid();

            // act
            book.CreateLimitOrder(id, TimeInForce.Day, Side.Buy, 100, 3);

            // assert
            Assert.IsNotNull(rejectedArgs);
            Assert.AreEqual(RejectReason.MarketClosed, rejectedArgs.Reason);
            Assert.AreEqual(id, rejectedArgs.OrderId);
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

            OrderCreateRejectedEventArgs rejectedArgs = null;
            book.OrderCreateRejected += (sender, e) => { rejectedArgs = e; };
            book.SetStatus(OrderBookStatus.Open);
            var id = Guid.NewGuid();

            // act
            book.CreateLimitOrder(id, TimeInForce.Day, Side.Buy, 100, quantity);

            // assert
            Assert.IsNotNull(rejectedArgs);
            Assert.AreEqual(RejectReason.InvalidQuantity, rejectedArgs.Reason);
            Assert.AreEqual(id, rejectedArgs.OrderId);
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

            OrderCreateRejectedEventArgs rejectedArgs = null;
            book.OrderCreateRejected += (sender, e) => { rejectedArgs = e; };
            book.SetStatus(OrderBookStatus.Open);
            var id = Guid.NewGuid();

            // act
            book.CreateLimitOrder(id, TimeInForce.Day, Side.Buy, price, 6);

            // assert
            Assert.IsNotNull(rejectedArgs);
            Assert.AreEqual(RejectReason.InvalidPriceIncrement, rejectedArgs.Reason);
            Assert.AreEqual(id, rejectedArgs.OrderId);
        }

        [Test]
        public void UpdateLimitOrder_Valid_Success()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now1 = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now1);
            var book = new OrderBook.OrderBook(sec, timeProvider);

            OrderUpdatedSuccessEventArgs updatedArgs = null;
            book.OrderUpdated += (sender, e) => { updatedArgs = e; };
            book.SetStatus(OrderBookStatus.Open);
            var id = Guid.NewGuid();
            book.CreateLimitOrder(id, TimeInForce.Day, Side.Buy, 100, 3);
            var now2 = new DateTime(2000, 1, 1, 12, 1, 0);
            timeProvider.SetCurrentTime(now2);

            // act
            book.UpdateLimitOrder(id, 110, 5);

            // assert
            Assert.IsNotNull(updatedArgs);
            Assert.AreEqual(id, updatedArgs.Order.Id);
            Assert.AreEqual(sec, updatedArgs.Order.Security);
            Assert.AreEqual(now1, updatedArgs.Order.CreatedTime);
            Assert.AreEqual(now2, updatedArgs.Order.ModifiedTime);
            Assert.IsNull(updatedArgs.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Working, updatedArgs.Order.Status);
            Assert.AreEqual(OrderType.Limit, updatedArgs.Order.Type);
            Assert.AreEqual(TimeInForce.Day, updatedArgs.Order.TimeInForce);
            Assert.AreEqual(Side.Buy, updatedArgs.Order.Side);
            Assert.AreEqual(110, updatedArgs.Order.Price);
            Assert.IsNull(updatedArgs.Order.StopPrice);
            Assert.AreEqual(5, updatedArgs.Order.Quantity);
            Assert.AreEqual(0, updatedArgs.Order.FilledQuantity);
            Assert.AreEqual(5, updatedArgs.Order.RemainingQuantity);
        }

        // TODO: increase qty
        // TODO: increase qty failed as order is filled
        // TODO: decrease qty failed as order is filled
        // TODO: decrease qty after partially filled

        [Test]
        public void UpdateLimitOrder_NotFound_Rejected()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now);
            var book = new OrderBook.OrderBook(sec, timeProvider);

            OrderUpdateRejectedEventArgs updateArgs = null;
            book.OrderUpdateRejected += (sender, e) => { updateArgs = e; };
            book.SetStatus(OrderBookStatus.Open);
            var id = Guid.NewGuid();

            // act
            book.UpdateLimitOrder(id, 110, 5);

            // assert
            Assert.IsNotNull(updateArgs);
            Assert.AreEqual(id, updateArgs.OrderId);
            Assert.AreEqual(RejectReason.OrderNotInBook, updateArgs.Reason);
        }

        [Test]
        public void UpdateLimitOrder_MarketClosed_Rejected()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now);
            var book = new OrderBook.OrderBook(sec, timeProvider);

            OrderUpdateRejectedEventArgs rejectedArgs = null;
            book.OrderUpdateRejected += (sender, e) => { rejectedArgs = e; };
            book.SetStatus(OrderBookStatus.Open);
            var id = Guid.NewGuid();
            book.CreateLimitOrder(id, TimeInForce.Day, Side.Buy, 100, 3);
            book.SetStatus(OrderBookStatus.Closed);

            // act
            book.UpdateLimitOrder(id, 105, 5);

            // assert
            Assert.IsNotNull(rejectedArgs);
            Assert.AreEqual(RejectReason.MarketClosed, rejectedArgs.Reason);
            Assert.AreEqual(id, rejectedArgs.OrderId);
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

            OrderUpdateRejectedEventArgs rejectedArgs = null;
            book.OrderUpdateRejected += (sender, e) => { rejectedArgs = e; };
            book.SetStatus(OrderBookStatus.Open);
            var id = Guid.NewGuid();
            book.CreateLimitOrder(id, TimeInForce.Day, Side.Buy, 100, 3);

            // act
            book.UpdateLimitOrder(id, 110, quantity);

            // assert
            Assert.IsNotNull(rejectedArgs);
            Assert.AreEqual(RejectReason.InvalidQuantity, rejectedArgs.Reason);
            Assert.AreEqual(id, rejectedArgs.OrderId);
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

            OrderUpdateRejectedEventArgs rejectedArgs = null;
            book.OrderUpdateRejected += (sender, e) => { rejectedArgs = e; };
            book.SetStatus(OrderBookStatus.Open);
            var id = Guid.NewGuid();
            book.CreateLimitOrder(id, TimeInForce.Day, Side.Buy, 100, 6);

            // act
            book.UpdateLimitOrder(id, price, 6);

            // assert
            Assert.IsNotNull(rejectedArgs);
            Assert.AreEqual(RejectReason.InvalidPriceIncrement, rejectedArgs.Reason);
            Assert.AreEqual(id, rejectedArgs.OrderId);
        }

        [Test]
        public void DeleteLimitOrder_Valid_Success()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now1 = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now1);
            var book = new OrderBook.OrderBook(sec, timeProvider);

            OrderDeletedSuccessEventArgs deletedArgs = null;
            book.OrderDeleted += (sender, e) => { deletedArgs = e; };
            book.SetStatus(OrderBookStatus.Open);
            var id = Guid.NewGuid();
            book.CreateLimitOrder(id, TimeInForce.Day, Side.Buy, 100, 3);
            var now2 = new DateTime(2000, 1, 1, 12, 1, 0);
            timeProvider.SetCurrentTime(now2);

            // act
            book.DeleteOrder(id);

            // assert
            Assert.IsNotNull(deletedArgs);
            Assert.AreEqual(id, deletedArgs.Order.Id);
            Assert.AreEqual(sec, deletedArgs.Order.Security);
            Assert.AreEqual(now1, deletedArgs.Order.CreatedTime);
            Assert.AreEqual(now1, deletedArgs.Order.ModifiedTime);
            Assert.AreEqual(now2, deletedArgs.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Deleted, deletedArgs.Order.Status);
            Assert.AreEqual(OrderType.Limit, deletedArgs.Order.Type);
            Assert.AreEqual(TimeInForce.Day, deletedArgs.Order.TimeInForce);
            Assert.AreEqual(Side.Buy, deletedArgs.Order.Side);
            Assert.AreEqual(100, deletedArgs.Order.Price);
            Assert.IsNull(deletedArgs.Order.StopPrice);
            Assert.AreEqual(3, deletedArgs.Order.Quantity);
            Assert.AreEqual(0, deletedArgs.Order.FilledQuantity);
            Assert.AreEqual(0, deletedArgs.Order.RemainingQuantity);
        }

        // TODO: verify order not found after deleted

        [Test]
        public void DeleteLimitOrder_NotFound_Rejected()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now);
            var book = new OrderBook.OrderBook(sec, timeProvider);

            OrderDeleteRejectedEventArgs deletedArgs = null;
            book.OrderDeleteRejected += (sender, e) => { deletedArgs = e; };
            book.SetStatus(OrderBookStatus.Open);
            var id = Guid.NewGuid();

            // act
            book.DeleteOrder(id);

            // assert
            Assert.IsNotNull(deletedArgs);
            Assert.AreEqual(id, deletedArgs.OrderId);
            Assert.AreEqual(RejectReason.OrderNotInBook, deletedArgs.Reason);
        }

        [Test]
        public void DeleteLimitOrder_MarketClosed_Rejected()
        {
            // arrange
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now = new DateTime(2000, 1, 1, 12, 0, 0);
            var timeProvider = new TestTimeProvider(now);
            var book = new OrderBook.OrderBook(sec, timeProvider);

            OrderDeleteRejectedEventArgs rejectedArgs = null;
            book.OrderDeleteRejected += (sender, e) => { rejectedArgs = e; };
            book.SetStatus(OrderBookStatus.Open);
            var id = Guid.NewGuid();
            book.CreateLimitOrder(id, TimeInForce.Day, Side.Buy, 100, 3);
            book.SetStatus(OrderBookStatus.Closed);

            // act
            book.DeleteOrder(id);

            // assert
            Assert.IsNotNull(rejectedArgs);
            Assert.AreEqual(RejectReason.MarketClosed, rejectedArgs.Reason);
            Assert.AreEqual(id, rejectedArgs.OrderId);
        }

        // Expire orders
        // Clear expired orders after status = closed

        // No match after status changed
        // Match after qty reduced
        // Match price updated
        // No match on deleted orders

        // Match on open

        // Market orders
        // Market orders exceptions
    }
}