using System;
using System.Linq;
using Circus.OrderBook;
using Circus.TimeProviders;
using NUnit.Framework;

namespace Circus.Tests.OrderBook
{
    [TestFixture]
    public class InMemoryOrderBookStopTriggerTests
    {
        private static readonly Security Sec = new("GCZ6", SecurityType.Future, 10, 10);

        private static readonly DateTime Now1 = new(2000, 1, 1, 12, 0, 0);
        private static readonly DateTime Now2 = new(2000, 1, 1, 12, 1, 0);
        private static readonly DateTime Now3 = new(2000, 1, 1, 12, 2, 0);
        private static readonly DateTime Now4 = new(2000, 1, 1, 12, 3, 0);
        private static readonly DateTime Now5 = new(2000, 1, 1, 12, 4, 0);
        private static readonly DateTime Now6 = new(2000, 1, 1, 12, 5, 0);

        private static readonly Guid ClientId1 = Guid.NewGuid();
        private static readonly Guid ClientId2 = Guid.NewGuid();
        private static readonly Guid ClientId3 = Guid.NewGuid();
        private static readonly Guid ClientId4 = Guid.NewGuid();
        private static readonly Guid ClientId5 = Guid.NewGuid();
        private static readonly Guid ClientId6 = Guid.NewGuid();

        private static readonly Guid OrderId1 = Guid.NewGuid();
        private static readonly Guid OrderId2 = Guid.NewGuid();
        private static readonly Guid OrderId3 = Guid.NewGuid();
        private static readonly Guid OrderId4 = Guid.NewGuid();
        private static readonly Guid OrderId5 = Guid.NewGuid();
        private static readonly Guid OrderId6 = Guid.NewGuid();

        private static TestTimeProvider TimeProvider;
        private static IOrderBook Book;

        [SetUp]
        public void SetUp()
        {
            TimeProvider = new TestTimeProvider(Now1);
            Book = new InMemoryOrderBook(Sec, TimeProvider);
        }

        [Test]
        public void BuyStopLimit_TriggersAndMatchesWhenPriceRisesToTrigger_Success()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3, 500);
            Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 3, 500); // last traded price -> 500

            TimeProvider.SetCurrentTime(Now3);
            var restingOffer = Book.CreateOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Sell, 5, 520);
            Assert.IsInstanceOf<CreateOrderConfirmed>(restingOffer[0]);

            TimeProvider.SetCurrentTime(Now4);
            var stopEvents = Book.CreateOrder(ClientId4, OrderId4, OrderValidity.Day, Side.Buy, 5, 530, 510);
            Assert.AreEqual(1, stopEvents.Count);
            Assert.IsInstanceOf<CreateOrderConfirmed>(stopEvents[0]);
            Assert.AreEqual(OrderStatus.Hidden, ((CreateOrderConfirmed) stopEvents[0]).Order.Status);

            TimeProvider.SetCurrentTime(Now5);
            var restingBid = Book.CreateOrder(ClientId5, OrderId5, OrderValidity.Day, Side.Sell, 2, 510); // to be hit
            Assert.IsInstanceOf<CreateOrderConfirmed>(restingBid[0]);

            // act - trade at the trigger price
            TimeProvider.SetCurrentTime(Now6);
            var events = Book.CreateOrder(ClientId6, OrderId6, OrderValidity.Day, Side.Buy, 2, 510);

            // assert
            var tradedAtTrigger = events.OfType<OrdersMatched>().FirstOrDefault(m => m.Price == 510);
            Assert.IsNotNull(tradedAtTrigger, "expected a trade at the trigger price of 510");

            var triggerConversion = events.OfType<UpdateOrderConfirmed>()
                .FirstOrDefault(u => u.Order.OrderId == OrderId4);
            Assert.IsNotNull(triggerConversion, "expected the stop order to be converted to a working limit order");
            Assert.AreEqual(OrderType.Limit, triggerConversion.Order.Type);
            Assert.AreEqual(OrderStatus.Working, triggerConversion.Order.Status);
            Assert.AreEqual(530, triggerConversion.Order.Price);

            var tradedAtLimit = events.OfType<OrdersMatched>().FirstOrDefault(m => m.Price == 520);
            Assert.IsNotNull(tradedAtLimit, "expected the newly triggered order to match against the resting offer at 520");
            Assert.AreEqual(5, tradedAtLimit.Quantity);
            Assert.IsTrue(tradedAtLimit.Fills.Any(f => f.Order.OrderId == OrderId4 && f.Order.Status == OrderStatus.Filled));
        }

        [Test]
        public void SellStopLimit_TriggersAndMatchesWhenPriceFallsToTrigger_Success()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3, 500);
            Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 3, 500); // last traded price -> 500

            TimeProvider.SetCurrentTime(Now3);
            var restingBid = Book.CreateOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Buy, 5, 480);
            Assert.IsInstanceOf<CreateOrderConfirmed>(restingBid[0]);

            TimeProvider.SetCurrentTime(Now4);
            var stopEvents = Book.CreateOrder(ClientId4, OrderId4, OrderValidity.Day, Side.Sell, 5, 470, 490);
            Assert.AreEqual(1, stopEvents.Count);
            Assert.IsInstanceOf<CreateOrderConfirmed>(stopEvents[0]);
            Assert.AreEqual(OrderStatus.Hidden, ((CreateOrderConfirmed) stopEvents[0]).Order.Status);

            TimeProvider.SetCurrentTime(Now5);
            var restingOffer = Book.CreateOrder(ClientId5, OrderId5, OrderValidity.Day, Side.Buy, 2, 490); // to be hit
            Assert.IsInstanceOf<CreateOrderConfirmed>(restingOffer[0]);

            // act - trade at the trigger price
            TimeProvider.SetCurrentTime(Now6);
            var events = Book.CreateOrder(ClientId6, OrderId6, OrderValidity.Day, Side.Sell, 2, 490);

            // assert
            var tradedAtTrigger = events.OfType<OrdersMatched>().FirstOrDefault(m => m.Price == 490);
            Assert.IsNotNull(tradedAtTrigger, "expected a trade at the trigger price of 490");

            var triggerConversion = events.OfType<UpdateOrderConfirmed>()
                .FirstOrDefault(u => u.Order.OrderId == OrderId4);
            Assert.IsNotNull(triggerConversion, "expected the stop order to be converted to a working limit order");
            Assert.AreEqual(OrderType.Limit, triggerConversion.Order.Type);
            Assert.AreEqual(OrderStatus.Working, triggerConversion.Order.Status);
            Assert.AreEqual(470, triggerConversion.Order.Price);

            var tradedAtLimit = events.OfType<OrdersMatched>().FirstOrDefault(m => m.Price == 480);
            Assert.IsNotNull(tradedAtLimit, "expected the newly triggered order to match against the resting bid at 480");
            Assert.AreEqual(5, tradedAtLimit.Quantity);
            Assert.IsTrue(tradedAtLimit.Fills.Any(f => f.Order.OrderId == OrderId4 && f.Order.Status == OrderStatus.Filled));
        }

        [Test]
        public void BuyStop_DoesNotTriggerWhenPriceMovesAwayFromTrigger_Success()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3, 500);
            Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 3, 500); // last traded price -> 500

            // buy stop above market: only triggers once price rises to/through 510
            TimeProvider.SetCurrentTime(Now3);
            Book.CreateOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Buy, 5, 520, 510);

            // act - trade at a lower price, moving further away from the trigger
            TimeProvider.SetCurrentTime(Now4);
            Book.CreateOrder(ClientId4, OrderId4, OrderValidity.Day, Side.Buy, 1, 490);
            TimeProvider.SetCurrentTime(Now5);
            var events = Book.CreateOrder(ClientId5, OrderId5, OrderValidity.Day, Side.Sell, 1, 490);

            // assert
            Assert.IsTrue(events.OfType<OrdersMatched>().Any(m => m.Price == 490));
            Assert.IsFalse(events.OfType<UpdateOrderConfirmed>().Any(u => u.Order.OrderId == OrderId3),
                "the stop order should still be pending, not triggered, since price moved away from the trigger");

            // the stop can still be cancelled while pending, proving it's still tracked correctly
            var cancelEvents = Book.CancelOrder(ClientId3, OrderId3);
            var cancelled = cancelEvents.OfType<CancelOrderConfirmed>().FirstOrDefault();
            Assert.IsNotNull(cancelled);
            Assert.AreEqual(OrderStatus.Cancelled, cancelled.Order.Status);
        }

        [Test]
        public void SellStop_DoesNotTriggerWhenPriceMovesAwayFromTrigger_Success()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3, 500);
            Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 3, 500); // last traded price -> 500

            // sell stop below market: only triggers once price falls to/through 490
            TimeProvider.SetCurrentTime(Now3);
            Book.CreateOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Sell, 5, 480, 490);

            // act - trade at a higher price, moving further away from the trigger
            TimeProvider.SetCurrentTime(Now4);
            Book.CreateOrder(ClientId4, OrderId4, OrderValidity.Day, Side.Buy, 1, 510);
            TimeProvider.SetCurrentTime(Now5);
            var events = Book.CreateOrder(ClientId5, OrderId5, OrderValidity.Day, Side.Sell, 1, 510);

            // assert
            Assert.IsTrue(events.OfType<OrdersMatched>().Any(m => m.Price == 510));
            Assert.IsFalse(events.OfType<UpdateOrderConfirmed>().Any(u => u.Order.OrderId == OrderId3),
                "the stop order should still be pending, not triggered, since price moved away from the trigger");

            // the stop can still be cancelled while pending, proving it's still tracked correctly
            var cancelEvents = Book.CancelOrder(ClientId3, OrderId3);
            var cancelled = cancelEvents.OfType<CancelOrderConfirmed>().FirstOrDefault();
            Assert.IsNotNull(cancelled);
            Assert.AreEqual(OrderStatus.Cancelled, cancelled.Order.Status);
        }

        [Test]
        public void StopMarketOrder_CancelledWhenOpposingBookEmptyOnTrigger_Success()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3, 500);
            Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 3, 500); // last traded price -> 500

            // buy stop market above market; when it triggers there must be resting sell orders for it to
            // convert into a priced limit order - here there are none, so it should be cancelled, not throw
            TimeProvider.SetCurrentTime(Now3);
            Book.CreateOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Buy, 5, null, 510);

            // act - trade at the trigger price with no resting sell orders left in the book afterwards
            TimeProvider.SetCurrentTime(Now4);
            Book.CreateOrder(ClientId4, OrderId4, OrderValidity.Day, Side.Sell, 2, 510);
            TimeProvider.SetCurrentTime(Now5);
            var events = Book.CreateOrder(ClientId5, OrderId5, OrderValidity.Day, Side.Buy, 2, 510);

            // assert
            Assert.IsTrue(events.OfType<OrdersMatched>().Any(m => m.Price == 510));

            var cancelled = events.OfType<CancelOrderConfirmed>().FirstOrDefault(c => c.Order.OrderId == OrderId3);
            Assert.IsNotNull(cancelled, "expected the triggered stop market order to be cancelled since the book was empty");
            Assert.AreEqual(OrderCancelledReason.NoOrdersToMatchMarketOrder, cancelled.Reason);
            Assert.AreEqual(OrderStatus.Cancelled, cancelled.Order.Status);

            // an order id is a permanent identity once it completes - reuse is rejected, not silently
            // accepted (see issue #1), and book state remains consistent either way
            TimeProvider.SetCurrentTime(Now6);
            var recreate = Book.CreateOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Buy, 1, 510);
            var rejected = recreate[0] as CreateOrderRejected;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(OrderRejectedReason.OrderIdAlreadyUsed, rejected.Reason);
        }

        [Test]
        public void MultipleBuyStops_AllTriggerWhenPriceGapsThroughTheirLevels_Success()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3, 500);
            Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 3, 500); // last traded price -> 500

            TimeProvider.SetCurrentTime(Now3);
            Book.CreateOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Buy, 1, 530, 510);

            TimeProvider.SetCurrentTime(Now4);
            Book.CreateOrder(ClientId4, OrderId4, OrderValidity.Day, Side.Buy, 1, 540, 520);

            TimeProvider.SetCurrentTime(Now5);
            Book.CreateOrder(ClientId5, OrderId5, OrderValidity.Day, Side.Sell, 2, 520); // resting offer for the gap trade

            // act - price gaps straight from 500 to 520, passing through both trigger levels
            TimeProvider.SetCurrentTime(Now6);
            var events = Book.CreateOrder(ClientId6, OrderId6, OrderValidity.Day, Side.Buy, 2, 520);

            // assert
            Assert.IsTrue(events.OfType<UpdateOrderConfirmed>().Any(u => u.Order.OrderId == OrderId3),
                "the 510 trigger stop should have fired");
            Assert.IsTrue(events.OfType<UpdateOrderConfirmed>().Any(u => u.Order.OrderId == OrderId4),
                "the 520 trigger stop should have fired");
        }
    }
}
