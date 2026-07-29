using System;
using Circus.OrderBook;
using Circus.TimeProviders;
using NUnit.Framework;

namespace Circus.Tests.OrderBook
{
    [TestFixture]
    public class InMemoryOrderBookPriceBandTests
    {
        private static readonly DateTime Now1 = new(2000, 1, 1, 12, 0, 0);

        private static readonly string CompanyId1 = "Company1";
        private static readonly string CompanyId2 = "Company2";
        private static readonly string CompanyId3 = "Company3";
        private static readonly string CompanyId4 = "Company4";

        private static readonly string OrderId1 = "Order1";
        private static readonly string OrderId2 = "Order2";
        private static readonly string OrderId3 = "Order3";
        private static readonly string OrderId4 = "Order4";

        private static TestTimeProvider TimeProvider;

        [SetUp]
        public void SetUp()
        {
            TimeProvider = new TestTimeProvider(Now1);
        }

        [Test]
        public void NoBandConfigured_OrderFarFromReferencePrice_Accepted()
        {
            // arrange - no restrictions at all, so banding is off even though a reference price is
            // seeded. A security without a band leaves the restriction out rather than configuring
            // one with no width.
            var security = new Security("GCZ6", SecurityType.Future, 10, 10);
            var book = new LevelTrackingOrderBook(security, TimeProvider);
            book.UpdateStatus(OrderBookStatus.Open, 100);

            // act
            var events = book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 5, 1000);

            // assert
            Assert.IsInstanceOf<CreateOrderConfirmed>(events[0]);
        }

        [Test]
        public void BothBandsConfigured_EachRestrictionGetsItsOwnThreshold()
        {
            // arrange - the entry band wide (1000) and the volatility band narrow (5). Each config
            // carries its own width, so the mapping in the book's constructor is the only place the
            // two could be crossed over. A wide entry band must not reject, and a narrow volatility
            // band must still pause on the resulting trade.
            var security = new Security("GCZ6", SecurityType.Future, 10, 10,
                PriceRestrictions: new PriceRestrictionConfig[]
                {
                    new OrderPriceBand(1000),
                    new VolatilityBand(5)
                });
            var book = new LevelTrackingOrderBook(security, TimeProvider);
            book.UpdateStatus(OrderBookStatus.Open, 100);

            // act - 200 is far outside the narrow volatility band but well inside the wide entry
            // band, so both orders are accepted and it is the trade between them that pauses
            var resting = book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 5, 200);
            var crossing = book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Buy, 5, 200);

            // assert
            Assert.IsInstanceOf<CreateOrderConfirmed>(resting[0],
                "entry band is 1000 wide - this order is nowhere near it");
            Assert.IsInstanceOf<CreateOrderConfirmed>(crossing[0]);
            Assert.AreEqual(OrderBookStatus.Paused, book.Status,
                "the 5-tick volatility band should have paused the book on the trade");
        }

        [Test]
        public void NoReferencePriceSeeded_NoTradeYet_BandInactive_Accepted()
        {
            // arrange - band is configured, but there's no anchor to check against yet
            var security = new Security("GCZ6", SecurityType.Future, 10, 10,
                PriceRestrictions: new PriceRestrictionConfig[] {new OrderPriceBand(5)});
            var book = new LevelTrackingOrderBook(security, TimeProvider);
            book.UpdateStatus(OrderBookStatus.Open);

            // act
            var events = book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 5, 1000);

            // assert
            Assert.IsInstanceOf<CreateOrderConfirmed>(events[0]);
        }

        [Test]
        public void ReferencePriceSeeded_OrderWithinBand_Accepted_OutsideBand_Rejected()
        {
            // arrange - band of 5 ticks (50) around a seeded reference of 100, so [50, 150]
            var security = new Security("GCZ6", SecurityType.Future, 10, 10,
                PriceRestrictions: new PriceRestrictionConfig[] {new OrderPriceBand(5)});
            var book = new LevelTrackingOrderBook(security, TimeProvider);
            book.UpdateStatus(OrderBookStatus.Open, 100);

            // act/assert - at the reference price
            var atReference = book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 5, 100);
            Assert.IsInstanceOf<CreateOrderConfirmed>(atReference[0]);

            // act/assert - exactly on the band edge, inclusive
            var atEdge = book.CreateLimitOrder(CompanyId1, OrderId2, new OrderValidity.Day(), Side.Buy, 5, 150);
            Assert.IsInstanceOf<CreateOrderConfirmed>(atEdge[0]);

            // act/assert - one tick beyond the edge
            var beyondEdge = book.CreateLimitOrder(CompanyId1, OrderId3, new OrderValidity.Day(), Side.Buy, 5, 160);
            var rejected = beyondEdge[0] as CreateOrderRejected;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(OrderRejectedReason.PriceOutsideBands, rejected.Reason);
        }

        [Test]
        public void BandMovesDynamically_AfterATrade_ReanchorsToNewLastTradedPrice()
        {
            // arrange - reference seeded at 100, band [50, 150]
            var security = new Security("GCZ6", SecurityType.Future, 10, 10,
                PriceRestrictions: new PriceRestrictionConfig[] {new OrderPriceBand(5)});
            var book = new LevelTrackingOrderBook(security, TimeProvider);
            book.UpdateStatus(OrderBookStatus.Open, 100);

            // 160 is outside the original [50, 150] band
            var beforeTrade = book.CreateLimitOrder(CompanyId2, OrderId1, new OrderValidity.Day(), Side.Sell, 5, 160);
            Assert.AreEqual(OrderRejectedReason.PriceOutsideBands, (beforeTrade[0] as CreateOrderRejected)?.Reason);

            // act - a trade prints at 140, re-anchoring the band to [90, 190]
            book.CreateLimitOrder(CompanyId1, OrderId2, new OrderValidity.Day(), Side.Buy, 5, 140);
            var matchEvents = book.CreateLimitOrder(CompanyId3, OrderId3, new OrderValidity.Day(), Side.Sell, 5, 140);
            Assert.IsInstanceOf<OrdersMatched>(matchEvents[^1]);

            // assert - 160 is now within the new [90, 190] band
            var afterTrade = book.CreateLimitOrder(CompanyId2, OrderId4, new OrderValidity.Day(), Side.Sell, 5, 160);
            Assert.IsInstanceOf<CreateOrderConfirmed>(afterTrade[0]);

            // assert - 60 was within the original [50, 150] band, but is now outside [90, 190]
            var nowOutOfBand = book.CreateLimitOrder(CompanyId4, "Order5", new OrderValidity.Day(), Side.Buy, 5, 60);
            Assert.AreEqual(OrderRejectedReason.PriceOutsideBands, (nowOutOfBand[0] as CreateOrderRejected)?.Reason);
        }

        [Test]
        public void UpdateOrder_RepriceOutsideBand_Rejected()
        {
            // arrange - reference seeded at 100, band [50, 150]
            var security = new Security("GCZ6", SecurityType.Future, 10, 10,
                PriceRestrictions: new PriceRestrictionConfig[] {new OrderPriceBand(5)});
            var book = new LevelTrackingOrderBook(security, TimeProvider);
            book.UpdateStatus(OrderBookStatus.Open, 100);
            book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 5, 100);

            // act - reprice to 200, outside the band
            var events = book.UpdateOrder(CompanyId1, OrderId2, OrderId1, price: 200);

            // assert
            var rejected = events[0] as UpdateOrderRejected;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(OrderRejectedReason.PriceOutsideBands, rejected.Reason);

            // the original order is untouched, still resting at 100
            var buyLevels = book.GetLevels(Side.Buy, 10);
            Assert.AreEqual(1, buyLevels.Count);
            Assert.AreEqual(100, buyLevels[0].Price);
        }
    }
}
