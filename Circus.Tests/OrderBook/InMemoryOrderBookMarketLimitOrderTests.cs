using System;
using Circus.OrderBook;
using Circus.TimeProviders;
using NUnit.Framework;

namespace Circus.Tests.OrderBook
{
    [TestFixture]
    public class InMemoryOrderBookMarketLimitOrderTests
    {
        private static readonly Security Sec = new("GCZ6", SecurityType.Future, 10, 10);

        private static readonly DateTime Now1 = new(2000, 1, 1, 12, 0, 0);
        private static readonly DateTime Now2 = new(2000, 1, 1, 12, 1, 0);

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

        [Test]
        public void FullFillAtBestLevel_Success()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Sell, 3, 100);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Buy, 3, marketLimit: true);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(2, events.Count);

            var created = events[0] as CreateOrderConfirmed;
            Assert.IsNotNull(created);
            Assert.AreEqual(OrderType.MarketLimit, created.Order.Type);
            Assert.AreEqual(100, created.Order.Price);

            var matched = events[1] as OrdersMatched;
            Assert.IsNotNull(matched);
            Assert.AreEqual(100, matched.Price);
            Assert.AreEqual(3, matched.Quantity);
            Assert.AreEqual(OrderType.MarketLimit, matched.Fills[1].Order.Type);
            Assert.AreEqual(OrderStatus.Filled, matched.Fills[1].Order.Status);
            Assert.AreEqual(3, matched.Fills[1].Order.FilledQuantity);
            Assert.AreEqual(0, matched.Fills[1].Order.RemainingQuantity);
        }

        [Test]
        public void PartialFillAtBestLevel_RestsAtBestPrice_TypeStaysMarketLimit()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Sell, 2, 100);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Buy, 5, marketLimit: true);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(2, events.Count);

            var matched = events[1] as OrdersMatched;
            Assert.IsNotNull(matched);
            Assert.AreEqual(100, matched.Price);
            Assert.AreEqual(2, matched.Quantity);

            var restingOrder = matched.Fills[1].Order;
            // stays reportable as MarketLimit at the order-entry level - neither this nor a
            // plain Market order collapses to Limit. Market data feeds like CME's MDP 3.0 never
            // expose order type either way, since TradeDataProducer/LevelDataProducer only ever
            // consume price/quantity/depth, never Order.Type.
            Assert.AreEqual(OrderType.MarketLimit, restingOrder.Type);
            Assert.AreEqual(OrderStatus.Working, restingOrder.Status);
            Assert.AreEqual(100, restingOrder.Price);
            Assert.AreEqual(2, restingOrder.FilledQuantity);
            Assert.AreEqual(3, restingOrder.RemainingQuantity);

            var levels = Book.GetLevels(Side.Buy, 10);
            Assert.AreEqual(1, levels.Count);
            Assert.AreEqual(100, levels[0].Price);
            Assert.AreEqual(3, levels[0].Quantity);
        }

        [Test]
        public void DoesNotSweepSecondLevel_UnlikePlainMarketOrder()
        {
            // arrange - a wide protection-tick band so a plain Market order provably reaches
            // the second level, contrasted against a MarketLimit order in the identical setup
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10, 20);

            var marketBook = new InMemoryOrderBook(sec, TimeProvider);
            marketBook.UpdateStatus(OrderBookStatus.Open);
            marketBook.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Sell, 2, 500);
            marketBook.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 10, 600);

            var marketLimitBook = new InMemoryOrderBook(sec, TimeProvider);
            marketLimitBook.UpdateStatus(OrderBookStatus.Open);
            marketLimitBook.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Sell, 2, 500);
            marketLimitBook.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 10, 600);

            TimeProvider.SetCurrentTime(Now2);

            // act
            var marketEvents = marketBook.CreateOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Buy, 5);
            var marketLimitEvents = marketLimitBook.CreateOrder(ClientId3, OrderId3, OrderValidity.Day, Side.Buy, 5,
                marketLimit: true);

            // assert - the plain Market order sweeps both levels and fully fills
            Assert.AreEqual(3, marketEvents.Count);
            var finalAggressor = ((OrdersMatched) marketEvents[2]).Fills[1].Order;
            Assert.AreEqual(OrderStatus.Filled, finalAggressor.Status);
            Assert.AreEqual(5, finalAggressor.FilledQuantity);
            Assert.AreEqual(0, finalAggressor.RemainingQuantity);

            // the market-limit order only touches the 500 level and rests the remainder there
            Assert.AreEqual(2, marketLimitEvents.Count);
            var matched = marketLimitEvents[1] as OrdersMatched;
            Assert.IsNotNull(matched);
            Assert.AreEqual(500, matched.Price);
            Assert.AreEqual(2, matched.Quantity);
            var restingOrder = matched.Fills[1].Order;
            Assert.AreEqual(OrderType.MarketLimit, restingOrder.Type);
            Assert.AreEqual(500, restingOrder.Price);
            Assert.AreEqual(2, restingOrder.FilledQuantity);
            Assert.AreEqual(3, restingOrder.RemainingQuantity);

            // the 600 level is completely untouched
            var remainingSellLevels = marketLimitBook.GetLevels(Side.Sell, 10);
            Assert.AreEqual(1, remainingSellLevels.Count);
            Assert.AreEqual(600, remainingSellLevels[0].Price);
            Assert.AreEqual(10, remainingSellLevels[0].Quantity);
        }

        [Test]
        public void EmptyBook_Rejected()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);

            // act
            var events = Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Buy, 3, marketLimit: true);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as CreateOrderRejected;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(OrderRejectedReason.NoOrdersToMatchMarketOrder, rejected.Reason);
        }

        [Test]
        public void FillAndKill_RemainderCancelledInsteadOfResting()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Sell, 2, 100);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.CreateOrder(ClientId2, OrderId2, OrderValidity.FillAndKill, Side.Buy, 5,
                marketLimit: true);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(3, events.Count);

            var matched = events[1] as OrdersMatched;
            Assert.IsNotNull(matched);
            Assert.AreEqual(100, matched.Price);
            Assert.AreEqual(2, matched.Quantity);

            var cancelled = events[2] as CancelOrderConfirmed;
            Assert.IsNotNull(cancelled);
            Assert.AreEqual(OrderCancelledReason.FillAndKillNotFilled, cancelled.Reason);
            Assert.AreEqual(OrderType.MarketLimit, cancelled.Order.Type);
            Assert.AreEqual(OrderStatus.Cancelled, cancelled.Order.Status);
            Assert.AreEqual(2, cancelled.Order.FilledQuantity);
            Assert.AreEqual(0, cancelled.Order.RemainingQuantity);

            Assert.AreEqual(0, Book.GetLevels(Side.Buy, 10).Count);
        }

        [Test]
        public void FillOrKill_InsufficientLiquidityAtBestLevelAlone_Rejected()
        {
            // arrange - 2 available at the best level, 10 more one level back; FOK must only
            // count the best level for a market-limit order, so this must be rejected even
            // though 12 combined would be enough for an order that could sweep
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Sell, 2, 100);
            Book.CreateOrder(ClientId2, OrderId2, OrderValidity.Day, Side.Sell, 10, 110);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.CreateOrder(ClientId3, OrderId3, OrderValidity.FillOrKill, Side.Buy, 5,
                marketLimit: true);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);
            var rejected = events[0] as CreateOrderRejected;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(OrderRejectedReason.InsufficientLiquidityForFillOrKill, rejected.Reason);

            // book is completely untouched
            var levels = Book.GetLevels(Side.Sell, 10);
            Assert.AreEqual(2, levels.Count);
            Assert.AreEqual(100, levels[0].Price);
            Assert.AreEqual(2, levels[0].Quantity);
        }

        [Test]
        public void Process_CreateOrder_MarketLimitFlagRoutesThrough()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(ClientId1, OrderId1, OrderValidity.Day, Side.Sell, 3, 100);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.Process(new CreateOrder(Sec, ClientId2, OrderId2, OrderValidity.Day, Side.Buy, 3,
                MarketLimit: true));

            // assert
            Assert.IsNotNull(events);
            var created = events[0] as CreateOrderConfirmed;
            Assert.IsNotNull(created);
            Assert.AreEqual(OrderType.MarketLimit, created.Order.Type);
        }
    }
}
