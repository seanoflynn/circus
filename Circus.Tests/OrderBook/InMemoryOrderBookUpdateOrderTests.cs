using System;
using Circus.OrderBook;
using Circus.TimeProviders;
using NUnit.Framework;

namespace Circus.Tests.OrderBook
{
    [TestFixture]
    public class InMemoryOrderBookUpdateOrderTests
    {
        private static readonly Security Sec = new("GCZ6", SecurityType.Future, 10, 10);

        private static readonly DateTime Now1 = new(2000, 1, 1, 12, 0, 0);
        private static readonly DateTime Now2 = new(2000, 1, 1, 12, 1, 0);
        private static readonly DateTime Now3 = new(2000, 1, 1, 12, 2, 0);
        
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
        
        [TestCase(5)]
        [TestCase(1)]
        public void LimitOrder_UpdateQuantity_Success(int quantity)
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3, 100);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.UpdateOrder(ClientId1, OrderId1, quantity);

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
            Assert.AreEqual(100, updated.Order.Price);
            Assert.IsNull(updated.Order.TriggerPrice);
            Assert.AreEqual(quantity, updated.Order.Quantity);
            Assert.AreEqual(0, updated.Order.FilledQuantity);
            Assert.AreEqual(quantity, updated.Order.RemainingQuantity);
        }
        
        [TestCase(5)]
        [TestCase(1)]
        public void StopOrder_UpdateQuantity_Success(int quantity)
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3, 100);
            Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 3, 100);
            Book.CreateOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Buy, 3, null, 110);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.UpdateOrder(ClientId3, OrderId3, quantity);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var updated = events[0] as UpdateOrderConfirmed;
            Assert.IsNotNull(updated);
            Assert.AreEqual(Sec, updated.Security);
            Assert.AreEqual(Now2, updated.Time);
            Assert.AreEqual(ClientId3, updated.ClientId);
            Assert.AreEqual(ClientId3, updated.Order.ClientId);
            Assert.AreEqual(OrderId3, updated.Order.OrderId);
            Assert.AreEqual(Sec, updated.Order.Security);
            Assert.AreEqual(Now1, updated.Order.CreatedTime);
            Assert.AreEqual(Now2, updated.Order.ModifiedTime);
            Assert.IsNull(updated.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Hidden, updated.Order.Status);
            Assert.AreEqual(OrderType.StopMarket, updated.Order.Type);
            Assert.AreEqual(OrderValidity.Day, updated.Order.OrderValidity);
            Assert.AreEqual(Side.Buy, updated.Order.Side);
            Assert.IsNull(updated.Order.Price);
            Assert.AreEqual(110, updated.Order.TriggerPrice);
            Assert.AreEqual(quantity, updated.Order.Quantity);
            Assert.AreEqual(0, updated.Order.FilledQuantity);
            Assert.AreEqual(quantity, updated.Order.RemainingQuantity);
        }
        
        [TestCase(Side.Buy, 110)]
        [TestCase(Side.Buy, 90)]
        [TestCase(Side.Sell, 110)]
        [TestCase(Side.Sell, 90)]
        public void LimitOrder_UpdatePrice_Success(Side side, decimal price)
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, side, 3, 100);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.UpdateOrder(ClientId1, OrderId1, price: price);

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
            Assert.AreEqual(side, updated.Order.Side);
            Assert.AreEqual(price, updated.Order.Price);
            Assert.IsNull(updated.Order.TriggerPrice);
            Assert.AreEqual(3, updated.Order.Quantity);
            Assert.AreEqual(0, updated.Order.FilledQuantity);
            Assert.AreEqual(3, updated.Order.RemainingQuantity);
        }
        
        [TestCase(Side.Buy, 130, 110, 120)]
        [TestCase(Side.Buy, 130, 110, 140)]
        [TestCase(Side.Sell, 70, 90, 60)]
        [TestCase(Side.Sell, 70, 90, 80)]
        public void StopOrder_UpdatePrice_Success(Side side, decimal price, decimal triggerPrice, decimal newPrice)
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3, 100);
            Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 3, 100);
            Book.CreateOrder(ClientId3, OrderId3, OrderValidity.Day, side, 3, triggerPrice, triggerPrice);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.UpdateOrder(ClientId3, OrderId3, price: newPrice);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var updated = events[0] as UpdateOrderConfirmed;
            Assert.IsNotNull(updated);
            Assert.AreEqual(Sec, updated.Security);
            Assert.AreEqual(Now2, updated.Time);
            Assert.AreEqual(ClientId3, updated.ClientId);
            Assert.AreEqual(ClientId3, updated.Order.ClientId);
            Assert.AreEqual(OrderId3, updated.Order.OrderId);
            Assert.AreEqual(Sec, updated.Order.Security);
            Assert.AreEqual(Now1, updated.Order.CreatedTime);
            Assert.AreEqual(Now2, updated.Order.ModifiedTime);
            Assert.IsNull(updated.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Hidden, updated.Order.Status);
            Assert.AreEqual(OrderType.StopLimit, updated.Order.Type);
            Assert.AreEqual(OrderValidity.Day, updated.Order.OrderValidity);
            Assert.AreEqual(side, updated.Order.Side);
            Assert.AreEqual(newPrice, updated.Order.Price);
            Assert.AreEqual(triggerPrice, updated.Order.TriggerPrice);
            Assert.AreEqual(3, updated.Order.Quantity);
            Assert.AreEqual(0, updated.Order.FilledQuantity);
            Assert.AreEqual(3, updated.Order.RemainingQuantity);
        }
        
        [TestCase(Side.Buy, 130, 120, 130)]
        [TestCase(Side.Buy, 130, 120, 110)]
        [TestCase(Side.Sell, 70, 80, 70)]
        [TestCase(Side.Sell, 70, 80, 90)]
        public void StopOrder_UpdateTriggerPrice_Success(Side side, decimal price, decimal triggerPrice,
            decimal newTriggerPrice)
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3, 100);
            Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 3, 100);
            Book.CreateOrder(ClientId3, OrderId3, OrderValidity.Day, side, 3, price, triggerPrice);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.UpdateOrder(ClientId3, OrderId3, triggerPrice: newTriggerPrice);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var updated = events[0] as UpdateOrderConfirmed;
            Assert.IsNotNull(updated);
            Assert.AreEqual(Sec, updated.Security);
            Assert.AreEqual(Now2, updated.Time);
            Assert.AreEqual(ClientId3, updated.ClientId);
            Assert.AreEqual(ClientId3, updated.Order.ClientId);
            Assert.AreEqual(OrderId3, updated.Order.OrderId);
            Assert.AreEqual(Sec, updated.Order.Security);
            Assert.AreEqual(Now1, updated.Order.CreatedTime);
            Assert.AreEqual(Now2, updated.Order.ModifiedTime);
            Assert.IsNull(updated.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Hidden, updated.Order.Status);
            Assert.AreEqual(OrderType.StopLimit, updated.Order.Type);
            Assert.AreEqual(OrderValidity.Day, updated.Order.OrderValidity);
            Assert.AreEqual(side, updated.Order.Side);
            Assert.AreEqual(price, updated.Order.Price);
            Assert.AreEqual(newTriggerPrice, updated.Order.TriggerPrice);
            Assert.AreEqual(3, updated.Order.Quantity);
            Assert.AreEqual(0, updated.Order.FilledQuantity);
            Assert.AreEqual(3, updated.Order.RemainingQuantity);
        }

        [Test]
        public void LimitOrder_UpdateTriggerPrice_Success()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3, 100);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.UpdateOrder(ClientId1, OrderId1, triggerPrice: 110);

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
            Assert.AreEqual(100, updated.Order.Price);
            Assert.IsNull(updated.Order.TriggerPrice);
            Assert.AreEqual(3, updated.Order.Quantity);
            Assert.AreEqual(0, updated.Order.FilledQuantity);
            Assert.AreEqual(3, updated.Order.RemainingQuantity);
        }
        
        [Test]
        public void UpdateStopMarketOrder_IncreaseQuantity_Success()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3, 100);
            Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 3, 100);
            Book.CreateOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Buy, 3, null, 110);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.UpdateOrder(ClientId3, OrderId3, 5);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var updated = events[0] as UpdateOrderConfirmed;
            Assert.IsNotNull(updated);
            Assert.AreEqual(Sec, updated.Security);
            Assert.AreEqual(Now2, updated.Time);
            Assert.AreEqual(ClientId3, updated.ClientId);
            Assert.AreEqual(ClientId3, updated.Order.ClientId);
            Assert.AreEqual(OrderId3, updated.Order.OrderId);
            Assert.AreEqual(Sec, updated.Order.Security);
            Assert.AreEqual(Now1, updated.Order.CreatedTime);
            Assert.AreEqual(Now2, updated.Order.ModifiedTime);
            Assert.IsNull(updated.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Hidden, updated.Order.Status);
            Assert.AreEqual(OrderType.StopMarket, updated.Order.Type);
            Assert.AreEqual(OrderValidity.Day, updated.Order.OrderValidity);
            Assert.AreEqual(Side.Buy, updated.Order.Side);
            Assert.IsNull(updated.Order.Price);
            Assert.AreEqual(110, updated.Order.TriggerPrice);
            Assert.AreEqual(5, updated.Order.Quantity);
            Assert.AreEqual(0, updated.Order.FilledQuantity);
            Assert.AreEqual(5, updated.Order.RemainingQuantity);
        }

        [Test]
        public void UpdateStopMarketOrder_DecreaseQuantity_Success()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3, 100);
            Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 3, 100);
            Book.CreateOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Sell, 3, null, 90);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.UpdateOrder(ClientId3, OrderId3, 1);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var updated = events[0] as UpdateOrderConfirmed;
            Assert.IsNotNull(updated);
            Assert.AreEqual(Sec, updated.Security);
            Assert.AreEqual(Now2, updated.Time);
            Assert.AreEqual(ClientId3, updated.ClientId);
            Assert.AreEqual(ClientId3, updated.Order.ClientId);
            Assert.AreEqual(OrderId3, updated.Order.OrderId);
            Assert.AreEqual(Sec, updated.Order.Security);
            Assert.AreEqual(Now1, updated.Order.CreatedTime);
            Assert.AreEqual(Now2, updated.Order.ModifiedTime);
            Assert.IsNull(updated.Order.CompletedTime);
            Assert.AreEqual(OrderStatus.Hidden, updated.Order.Status);
            Assert.AreEqual(OrderType.StopMarket, updated.Order.Type);
            Assert.AreEqual(OrderValidity.Day, updated.Order.OrderValidity);
            Assert.AreEqual(Side.Sell, updated.Order.Side);
            Assert.IsNull(updated.Order.Price);
            Assert.AreEqual(90, updated.Order.TriggerPrice);
            Assert.AreEqual(1, updated.Order.Quantity);
            Assert.AreEqual(0, updated.Order.FilledQuantity);
            Assert.AreEqual(1, updated.Order.RemainingQuantity);
        }
        
        [Test]
        public void DecreaseQuantityBelowFilled_Cancelled()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 5, 100);
            Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 4, 100);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.UpdateOrder(ClientId1, OrderId1, 2, 100);

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
            Assert.IsNull(cancelled.Order.TriggerPrice);
            Assert.AreEqual(5, cancelled.Order.Quantity);
            Assert.AreEqual(4, cancelled.Order.FilledQuantity);
            Assert.AreEqual(0, cancelled.Order.RemainingQuantity);
        }

        [Test]
        public void MatchAgainstOrderByTime()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Sell, 5, 90);
            TimeProvider.SetCurrentTime(Now2);
            Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Buy, 5, 80);
            TimeProvider.SetCurrentTime(Now3);

            // act
            var events = Book.UpdateOrder(ClientId1, OrderId1, 3, 70);

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
            Assert.IsNull(matched.Fills[0].Order.TriggerPrice);
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
            Assert.IsNull(matched.Fills[1].Order.TriggerPrice);
            Assert.AreEqual(3, matched.Fills[1].Order.Quantity);
            Assert.AreEqual(3, matched.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(0, matched.Fills[1].Order.RemainingQuantity);
        }

        [Test]
        public void MarketClosed_Rejected()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3, 100);
            Book.UpdateStatus(OrderBookStatus.Closed);

            // act
            var events = Book.UpdateOrder(ClientId1, OrderId1, null, 105, 5);

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
        
        [Test]
        public void NoChange_Rejected()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3, 100);

            // act
            var events = Book.UpdateOrder(ClientId1, OrderId1);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as UpdateOrderRejected;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(Sec, rejected.Security);
            Assert.AreEqual(Now1, rejected.Time);
            Assert.AreEqual(ClientId1, rejected.ClientId);
            Assert.AreEqual(OrderId1, rejected.OrderId);
            Assert.AreEqual(OrderRejectedReason.NoChange, rejected.Reason);
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void InvalidQuantity_Rejected(int quantity)
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3, 100);

            // act
            var events = Book.UpdateOrder(ClientId1, OrderId1, quantity, 110);

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
        public void InvalidPriceIncrement_Rejected(decimal price)
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 6, 100);

            // act
            var events = Book.UpdateOrder(ClientId1, OrderId1, 6, price);

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

        [TestCase(8)]
        [TestCase(-8)]
        [TestCase(-108)]
        [TestCase(10.01)]
        public void InvalidTriggerPriceIncrement_Rejected(decimal triggerPrice)
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 6, null, 100);

            // act
            var events = Book.UpdateOrder(ClientId1, OrderId1, 6, null, triggerPrice);

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
        public void Filled_Rejected()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3, 100);
            Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 3, 100);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.UpdateOrder(ClientId1, OrderId1, 5, 110);

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
        public void LimitOrder_Cancelled_Rejected()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3, 100);
            Book.CancelOrder(ClientId1, OrderId1);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.UpdateOrder(ClientId1, OrderId1, 5, 110);

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
        public void LimitOrder_Expired_Rejected()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3, 100);
            Book.UpdateStatus(OrderBookStatus.Closed);
            Book.UpdateStatus(OrderBookStatus.Open);

            // act
            var events = Book.UpdateOrder(ClientId1, OrderId1, 5, 110);

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
        public void StopOrder_Cancelled_Rejected()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3, 90);
            Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 3, 90);
            Book.CreateOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Buy, 3, null, 100);
            Book.CancelOrder(ClientId3, OrderId3);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.UpdateOrder(ClientId3, OrderId3, 5);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as UpdateOrderRejected;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(Sec, rejected.Security);
            Assert.AreEqual(Now2, rejected.Time);
            Assert.AreEqual(ClientId3, rejected.ClientId);
            Assert.AreEqual(OrderId3, rejected.OrderId);
            Assert.AreEqual(OrderRejectedReason.TooLateToCancel, rejected.Reason);
        }

        [Test]
        public void StopOrder_Expired_Rejected()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3, 90);
            Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 3, 90);
            Book.CreateOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Buy, 3, null, 100);
            Book.UpdateStatus(OrderBookStatus.Closed);
            Book.UpdateStatus(OrderBookStatus.Open);

            // act
            var events = Book.UpdateOrder(ClientId3, OrderId3, 5, null, 110);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as UpdateOrderRejected;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(Sec, rejected.Security);
            Assert.AreEqual(Now1, rejected.Time);
            Assert.AreEqual(ClientId3, rejected.ClientId);
            Assert.AreEqual(OrderId3, rejected.OrderId);
            Assert.AreEqual(OrderRejectedReason.TooLateToCancel, rejected.Reason);
        }
        
        // TODO: trigger stop order cancelled + expired
        
        [Test]
        public void NotFound_Rejected()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);

            // act
            var events = Book.UpdateOrder(ClientId1, OrderId1, 5, 110);

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
        public void UpdateTriggerPrice_TriggerPriceMustBeLessThanPrice_Rejected()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3, 90);
            Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 3, 90);
            Book.CreateOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Buy, 3, 110, 100);

            // act
            var events = Book.UpdateOrder(ClientId3, OrderId3, triggerPrice: 120);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as UpdateOrderRejected;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(Sec, rejected.Security);
            Assert.AreEqual(Now1, rejected.Time);
            Assert.AreEqual(ClientId3, rejected.ClientId);
            Assert.AreEqual(OrderId3, rejected.OrderId);
            Assert.AreEqual(OrderRejectedReason.TriggerPriceMustBeLessThanPrice, rejected.Reason);
        }
        
        [Test]
        public void UpdateTriggerPrice_TriggerPriceMustBeGreaterThanPrice_Rejected()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3, 90);
            Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 3, 90);
            Book.CreateOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Sell, 3, 70, 80);

            // act
            var events = Book.UpdateOrder(ClientId3, OrderId3, triggerPrice: 60);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as UpdateOrderRejected;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(Sec, rejected.Security);
            Assert.AreEqual(Now1, rejected.Time);
            Assert.AreEqual(ClientId3, rejected.ClientId);
            Assert.AreEqual(OrderId3, rejected.OrderId);
            Assert.AreEqual(OrderRejectedReason.TriggerPriceMustBeGreaterThanPrice, rejected.Reason);
        }

        [Test]
        public void UpdatePrice_TriggerPriceMustBeLessThanPrice_Rejected()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3, 90);
            Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 3, 90);
            Book.CreateOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Buy, 3, 110, 110);

            // act
            var events = Book.UpdateOrder(ClientId3, OrderId3, price: 100);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as UpdateOrderRejected;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(Sec, rejected.Security);
            Assert.AreEqual(Now1, rejected.Time);
            Assert.AreEqual(ClientId3, rejected.ClientId);
            Assert.AreEqual(OrderId3, rejected.OrderId);
            Assert.AreEqual(OrderRejectedReason.TriggerPriceMustBeLessThanPrice, rejected.Reason);
        }
        
        [Test]
        public void UpdatePrice_TriggerPriceMustBeGreaterThanPrice_Rejected()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3, 90);
            Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 3, 90);
            Book.CreateOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Sell, 3, 70, 70);

            // act
            var events = Book.UpdateOrder(ClientId3, OrderId3, price: 80);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as UpdateOrderRejected;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(Sec, rejected.Security);
            Assert.AreEqual(Now1, rejected.Time);
            Assert.AreEqual(ClientId3, rejected.ClientId);
            Assert.AreEqual(OrderId3, rejected.OrderId);
            Assert.AreEqual(OrderRejectedReason.TriggerPriceMustBeGreaterThanPrice, rejected.Reason);
        }
        
        [TestCase(90)]
        [TestCase(100)]
        public void StopOrder_TriggerPriceMustBeLessThanLastTraded_Rejected(decimal triggerPrice)
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3, 90);
            Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 3, 90);
            Book.CreateOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Sell, 3, 70, 70);
            
            // act
            var events = Book.UpdateOrder(ClientId3, OrderId3, triggerPrice: triggerPrice);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as UpdateOrderRejected;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(Sec, rejected.Security);
            Assert.AreEqual(Now1, rejected.Time);
            Assert.AreEqual(ClientId3, rejected.ClientId);
            Assert.AreEqual(OrderId3, rejected.OrderId);
            Assert.AreEqual(OrderRejectedReason.TriggerPriceMustBeLessThanLastTradedPrice, rejected.Reason);
        }

        [TestCase(90)]
        [TestCase(80)]
        public void StopOrder_TriggerPriceMustBeGreaterThanLastTraded_Rejected(decimal triggerPrice)
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3, 90);
            Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 3, 90);
            Book.CreateOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Buy, 3, 110, 110);

            // act
            var events = Book.UpdateOrder(ClientId3, OrderId3, triggerPrice: triggerPrice);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as UpdateOrderRejected;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(Sec, rejected.Security);
            Assert.AreEqual(Now1, rejected.Time);
            Assert.AreEqual(ClientId3, rejected.ClientId);
            Assert.AreEqual(OrderId3, rejected.OrderId);
            Assert.AreEqual(OrderRejectedReason.TriggerPriceMustBeGreaterThanLastTradedPrice, rejected.Reason);
        }
    }
}