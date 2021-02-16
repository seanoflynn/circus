using System;
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
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now = DateTime.UtcNow;
            var timeProvider = new TestTimeProvider(now);
            var book = new OrderBook.OrderBook(sec, timeProvider);

            OrderCreatedSuccessEventArgs createdArgs = null;
            book.OrderCreated += (sender, e) => { createdArgs = e; };

            book.SetStatus(OrderBookStatus.Open);
            var id = Guid.NewGuid();
            book.CreateLimitOrder(id, TimeInForce.Day, Side.Buy, 100, 3);

            Assert.IsNotNull(createdArgs);
            Assert.AreEqual(id, createdArgs.Order.Id);
            Assert.AreEqual(sec, createdArgs.Order.Security);
            Assert.AreEqual(now, createdArgs.Order.CreatedTime);
            Assert.AreEqual(now, createdArgs.Order.ModifiedTime);
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
        public void CreateLimitOrder_MarketClosed_Rejected()
        {
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now = DateTime.UtcNow;
            var timeProvider = new TestTimeProvider(now);
            var book = new OrderBook.OrderBook(sec, timeProvider);

            OrderCreateRejectedEventArgs rejectedArgs = null;
            book.OrderCreateRejected += (sender, e) => { rejectedArgs = e; };

            book.SetStatus(OrderBookStatus.Closed);
            var id = Guid.NewGuid();
            book.CreateLimitOrder(id, TimeInForce.Day, Side.Buy, 100, 3);

            Assert.IsNotNull(rejectedArgs);
            Assert.AreEqual(RejectReason.MarketClosed, rejectedArgs.Reason);
            Assert.AreEqual(id, rejectedArgs.OrderId);
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void CreateLimitOrder_InvalidQuantity_Rejected(int quantity)
        {
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now = DateTime.UtcNow;
            var timeProvider = new TestTimeProvider(now);
            var book = new OrderBook.OrderBook(sec, timeProvider);

            OrderCreateRejectedEventArgs rejectedArgs = null;
            book.OrderCreateRejected += (sender, e) => { rejectedArgs = e; };

            book.SetStatus(OrderBookStatus.Open);
            var id = Guid.NewGuid();
            book.CreateLimitOrder(id, TimeInForce.Day, Side.Buy, 100, quantity);

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
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now = DateTime.UtcNow;
            var timeProvider = new TestTimeProvider(now);
            var book = new OrderBook.OrderBook(sec, timeProvider);

            OrderCreateRejectedEventArgs rejectedArgs = null;
            book.OrderCreateRejected += (sender, e) => { rejectedArgs = e; };

            book.SetStatus(OrderBookStatus.Open);
            var id = Guid.NewGuid();
            book.CreateLimitOrder(id, TimeInForce.Day, Side.Buy, price, 6);

            Assert.IsNotNull(rejectedArgs);
            Assert.AreEqual(RejectReason.InvalidPriceIncrement, rejectedArgs.Reason);
            Assert.AreEqual(id, rejectedArgs.OrderId);
        }
        
        [Test]
        public void UpdateLimitOrder_Valid_Success()
        {
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now1 = DateTime.UtcNow;
            var timeProvider = new TestTimeProvider(now1);
            var book = new OrderBook.OrderBook(sec, timeProvider);

            OrderUpdatedSuccessEventArgs updatedArgs = null;
            book.OrderUpdated += (sender, e) => { updatedArgs = e; };

            book.SetStatus(OrderBookStatus.Open);
            var id = Guid.NewGuid();
            book.CreateLimitOrder(id, TimeInForce.Day, Side.Buy, 100, 3);
            var now2 = DateTime.UtcNow;
            timeProvider.SetCurrentTime(now2);
            book.UpdateLimitOrder(id, 110, 5);

            Assert.IsNotNull(updatedArgs);
            Assert.AreEqual(id, updatedArgs.Order.Id);
            Assert.AreEqual(sec, updatedArgs.Order.Security);
            Assert.AreEqual(now1, updatedArgs.Order.CreatedTime);
            Assert.AreEqual(now2, updatedArgs.Order.ModifiedTime);
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

        [Test]
        public void UpdateLimitOrder_NotFound_Rejected()
        {
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now = DateTime.UtcNow;
            var timeProvider = new TestTimeProvider(now);
            var book = new OrderBook.OrderBook(sec, timeProvider);

            OrderUpdateRejectedEventArgs updateArgs = null;
            book.OrderUpdateRejected += (sender, e) => { updateArgs = e; };

            book.SetStatus(OrderBookStatus.Open);
            var id = Guid.NewGuid();
            book.UpdateLimitOrder(id, 110, 5);

            Assert.IsNotNull(updateArgs);
            Assert.AreEqual(id, updateArgs.OrderId);
            Assert.AreEqual(RejectReason.OrderNotInBook, updateArgs.Reason);
        }
        
        [Test]
        public void UpdateLimitOrder_MarketClosed_Rejected()
        {
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now = DateTime.UtcNow;
            var timeProvider = new TestTimeProvider(now);
            var book = new OrderBook.OrderBook(sec, timeProvider);

            OrderUpdateRejectedEventArgs rejectedArgs = null;
            book.OrderUpdateRejected += (sender, e) => { rejectedArgs = e; };

            book.SetStatus(OrderBookStatus.Open);
            var id = Guid.NewGuid();
            book.CreateLimitOrder(id, TimeInForce.Day, Side.Buy, 100, 3);
            book.SetStatus(OrderBookStatus.Closed);
            book.UpdateLimitOrder(id, 105, 5);

            Assert.IsNotNull(rejectedArgs);
            Assert.AreEqual(RejectReason.MarketClosed, rejectedArgs.Reason);
            Assert.AreEqual(id, rejectedArgs.OrderId);
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void UpdateLimitOrder_InvalidQuantity_Rejected(int quantity)
        {
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now = DateTime.UtcNow;
            var timeProvider = new TestTimeProvider(now);
            var book = new OrderBook.OrderBook(sec, timeProvider);

            OrderUpdateRejectedEventArgs rejectedArgs = null;
            book.OrderUpdateRejected += (sender, e) => { rejectedArgs = e; };

            book.SetStatus(OrderBookStatus.Open);
            var id = Guid.NewGuid();
            book.CreateLimitOrder(id, TimeInForce.Day, Side.Buy, 100, 3);
            book.UpdateLimitOrder(id, 110, quantity);

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
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now = DateTime.UtcNow;
            var timeProvider = new TestTimeProvider(now);
            var book = new OrderBook.OrderBook(sec, timeProvider);

            OrderUpdateRejectedEventArgs rejectedArgs = null;
            book.OrderUpdateRejected += (sender, e) => { rejectedArgs = e; };

            book.SetStatus(OrderBookStatus.Open);
            var id = Guid.NewGuid();
            book.CreateLimitOrder(id, TimeInForce.Day, Side.Buy, 100, 6);
            book.UpdateLimitOrder(id, price, 6);

            Assert.IsNotNull(rejectedArgs);
            Assert.AreEqual(RejectReason.InvalidPriceIncrement, rejectedArgs.Reason);
            Assert.AreEqual(id, rejectedArgs.OrderId);
        }

        [Test]
        public void DeleteLimitOrder_Valid_Success()
        {
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now1 = DateTime.UtcNow;
            var timeProvider = new TestTimeProvider(now1);
            var book = new OrderBook.OrderBook(sec, timeProvider);

            OrderDeletedSuccessEventArgs deletedArgs = null;
            book.OrderDeleted += (sender, e) => { deletedArgs = e; };

            book.SetStatus(OrderBookStatus.Open);
            var id = Guid.NewGuid();
            book.CreateLimitOrder(id, TimeInForce.Day, Side.Buy, 100, 3);
            var now2 = DateTime.UtcNow;
            timeProvider.SetCurrentTime(now2);
            
            book.DeleteOrder(id);

            Assert.IsNotNull(deletedArgs);
            Assert.AreEqual(id, deletedArgs.Order.Id);
            Assert.AreEqual(sec, deletedArgs.Order.Security);
            Assert.AreEqual(now1, deletedArgs.Order.CreatedTime);
            Assert.AreEqual(now2, deletedArgs.Order.ModifiedTime);
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

        [Test]
        public void DeleteLimitOrder_NotFound_Rejected()
        {
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now = DateTime.UtcNow;
            var timeProvider = new TestTimeProvider(now);
            var book = new OrderBook.OrderBook(sec, timeProvider);

            OrderDeleteRejectedEventArgs deletedArgs = null;
            book.OrderDeleteRejected += (sender, e) => { deletedArgs = e; };

            book.SetStatus(OrderBookStatus.Open);
            var id = Guid.NewGuid();
            book.DeleteOrder(id);

            Assert.IsNotNull(deletedArgs);
            Assert.AreEqual(id, deletedArgs.OrderId);
            Assert.AreEqual(RejectReason.OrderNotInBook, deletedArgs.Reason);
        }

        [Test]
        public void DeleteLimitOrder_MarketClosed_Rejected()
        {
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now = DateTime.UtcNow;
            var timeProvider = new TestTimeProvider(now);
            var book = new OrderBook.OrderBook(sec, timeProvider);

            OrderDeleteRejectedEventArgs rejectedArgs = null;
            book.OrderDeleteRejected += (sender, e) => { rejectedArgs = e; };

            book.SetStatus(OrderBookStatus.Open);
            var id = Guid.NewGuid();
            book.CreateLimitOrder(id, TimeInForce.Day, Side.Buy, 100, 3);
            book.SetStatus(OrderBookStatus.Closed);
            book.DeleteOrder(id);

            Assert.IsNotNull(rejectedArgs);
            Assert.AreEqual(RejectReason.MarketClosed, rejectedArgs.Reason);
            Assert.AreEqual(id, rejectedArgs.OrderId);
        }
        
        // Expire orders
        
        // Match exact quantity
        // Match remainder qty on aggressor
        // Match remainder qty on passive
        // Match multiple times
        // Match multiple + clear book
        // Match different tick sizes

        // Match after status
        // Match after qty reduced
        // Match price updated
        // No match on deleted orders

        // Clear expired orders after status = closed
        // Match on open (?) - different tick sizes

        // Market orders
        // Market orders exceptions
        
        [Test]
        public void LimitOrder_Filled_Success()
        {
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);
            var now1 = DateTime.UtcNow;
            var timeProvider = new TestTimeProvider(now1);
            var book = new OrderBook.OrderBook(sec, timeProvider);
        	book.SetStatus(OrderBookStatus.Open);
        
        	Fill fill1 = null;
        	Fill fill2 = null;
            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();
        	book.OrderFilled += (sender, e) =>
            {
                if (e.Fill.OrderId == id1) fill1 = e.Fill;
                if (e.Fill.OrderId == id2) fill2 = e.Fill;
            };
        
        	book.CreateLimitOrder(id1, TimeInForce.Day,  Side.Buy, 100, 3);
            var now2 = DateTime.UtcNow;
            timeProvider.SetCurrentTime(now2);
        	book.CreateLimitOrder(id2, TimeInForce.Day,  Side.Sell, 100, 5);
        
        	Assert.IsNotNull(fill1);
            Assert.AreEqual(id1, fill1.OrderId);
            Assert.AreEqual(now2, fill1.Time);
            Assert.AreEqual(100, fill1.Price);
            Assert.AreEqual(Side.Buy, fill1.Side);
            Assert.AreEqual(3, fill1.Quantity);
            Assert.IsFalse(fill1.IsAggressor);
            // TODO: order?
        
            Assert.IsNotNull(fill2);
            Assert.AreEqual(id2, fill2.OrderId);
            Assert.AreEqual(now2, fill2.Time);
            Assert.AreEqual(100, fill2.Price);
            Assert.AreEqual(3, fill2.Quantity);
            Assert.IsTrue(fill2.IsAggressor);
            // TODO: order?
        }
        
        // public void OrderExpired()
        // {
        // 	var sec = new Security(Id: 1, Type: SecurityType.Future, Group: "GC", Product: "GC", Contract: "GCZ6");
        // 	var book = new OrderBook(sec);
        // 	book.SetStatus(OrderBookStatus.Open);
        //
        // 	OrderExpiredEventArgs args = null;
        // 	book.OrderExpired += (sender, e) => { args = e; };
        //
        // 	book.CreateLimitOrder(2, TimeInForce.Day, null, Side.Buy, 100, 3);
        // 	book.SetStatus(OrderBookStatus.Close);
        //
        // 	Debug.Assert(args != null);
        // 	Debug.Assert(args.Order.Status == OrderStatus.Expired);
        // 	Debug.Assert(args.Order.Id == 2);
        // 	Debug.Assert(args.Order.Price == 100);
        // 	Debug.Assert(args.Order.FilledQuantity == 0);
        // 	Debug.Assert(args.Order.RemainingQuantity == 0);
        // }
        //
        // public void OrderTraded()
        // {
        // 	var sec = new Security(Id: 1, Type: SecurityType.Future, Group: "GC", Product: "GC", Contract: "GCZ6");
        // 	var book = new OrderBook(sec);
        // 	book.SetStatus(OrderBookStatus.Open);
        //
        // 	TradedEventArgs firedArgs = null;
        // 	book.Traded += (o, e) => { firedArgs = e; };
        //
        // 	book.CreateLimitOrder(1, TimeInForce.Day, null, Side.Buy, 100, 2);
        // 	book.CreateLimitOrder(3, TimeInForce.Day, null, Side.Sell, 100, 5);
        //
        // 	Debug.Assert(firedArgs != null);
        // 	//Debug.Assert(firedArgs.Price == 100);
        // 	//Debug.Assert(firedArgs.AggressorSide == Side.Sell);
        // 	//Debug.Assert(firedArgs.Quantity == 5);
        // }
    }
}