using System;
using System.Linq;
using Circus.OrderBook;
using Circus.TimeProviders;
using NUnit.Framework;

namespace Circus.Tests.OrderBook
{
    [TestFixture]
    public class InMemoryOrderBookAuctionTests
    {
        private static readonly DateTime Now1 = new(2000, 1, 1, 12, 0, 0);
        private static readonly DateTime Now2 = new(2000, 1, 1, 12, 1, 0);
        private static readonly DateTime Now3 = new(2000, 1, 1, 12, 2, 0);
        private static readonly DateTime Now4 = new(2000, 1, 1, 12, 3, 0);

        private static readonly string CompanyId1 = "Company1";
        private static readonly string CompanyId2 = "Company2";
        private static readonly string CompanyId3 = "Company3";
        private static readonly string CompanyId4 = "Company4";
        private static readonly string CompanyId5 = "Company5";
        private static readonly string CompanyId6 = "Company6";

        private static readonly string OrderId1 = "Order1";
        private static readonly string OrderId2 = "Order2";
        private static readonly string OrderId3 = "Order3";
        private static readonly string OrderId4 = "Order4";
        private static readonly string OrderId5 = "Order5";
        private static readonly string OrderId6 = "Order6";
        private static readonly string OrderId7 = "Order7";
        private static readonly string OrderId8 = "Order8";
        private static readonly string CancelId1 = "Cancel1";

        private static TestTimeProvider TimeProvider;

        [SetUp]
        public void SetUp()
        {
            TimeProvider = new TestTimeProvider(Now1);
        }

        [Test]
        public void Opening_PicksMaxVolumePrice_AcrossMultipleLevels_WithPriceImprovement()
        {
            // arrange - best bid 140 vs best offer 110 would only cross 5 at the touch, but the
            // volume-maximizing single price is 120, clearing 11: the 140 and 130 buys fully fill
            // (price improvement - they pay 120, not their own higher limit), the 110 and 120
            // sells fully fill (also price improvement - they get 120, not their own lower limit),
            // and the 120 buy (the marginal order) only partially fills for the 1 unit left over
            var security = new Security("GCZ6", SecurityType.Future, 10, 10);
            var book = new InMemoryOrderBook(security, TimeProvider);
            book.UpdateStatus(OrderBookStatus.PreOpen);

            book.CreateOrder(CompanyId1, OrderId1, OrderValidity.Day, Side.Buy, 3, 140);
            book.CreateOrder(CompanyId2, OrderId2, OrderValidity.Day, Side.Buy, 7, 130);
            book.CreateOrder(CompanyId3, OrderId3, OrderValidity.Day, Side.Buy, 15, 120);
            book.CreateOrder(CompanyId4, OrderId4, OrderValidity.Day, Side.Sell, 5, 110);
            book.CreateOrder(CompanyId5, OrderId5, OrderValidity.Day, Side.Sell, 6, 120);
            book.CreateOrder(CompanyId6, OrderId6, OrderValidity.Day, Side.Sell, 20, 130);

            // act
            var events = book.UpdateStatus(OrderBookStatus.Open);

            // assert - every fill in this batch prints at the single auction price of 120
            Assert.IsInstanceOf<StatusChanged>(events[0]);
            for (var i = 1; i < events.Count; i++)
            {
                var matched = events[i] as OrdersMatched;
                Assert.IsNotNull(matched);
                Assert.AreEqual(120, matched.Price);
            }

            var totalQuantity = 0;
            for (var i = 1; i < events.Count; i++)
                totalQuantity += ((OrdersMatched) events[i]).Quantity;
            Assert.AreEqual(11, totalQuantity);

            // the marginal buy order (120) is left resting, partially filled
            var buyLevels = book.GetLevels(Side.Buy, 10);
            Assert.AreEqual(1, buyLevels.Count);
            Assert.AreEqual(120, buyLevels[0].Price);
            Assert.AreEqual(14, buyLevels[0].Quantity);

            // the 110 and 120 sells fully cleared (11 total); the 130 sell (outside the clearing
            // price's crossing range) is untouched
            var sellLevels = book.GetLevels(Side.Sell, 10);
            Assert.AreEqual(1, sellLevels.Count);
            Assert.AreEqual(130, sellLevels[0].Price);
            Assert.AreEqual(20, sellLevels[0].Quantity);
        }

        [Test]
        public void Opening_TiedCandidates_DefaultsToCmeDirectionRule()
        {
            // arrange - 120 and 130 tie for max executable volume (10 each), with the surplus on
            // the sell side at both - CME's rule picks the lowest of the tied prices in that case
            var security = new Security("GCZ6", SecurityType.Future, 10, 10);
            var book = new InMemoryOrderBook(security, TimeProvider);
            book.UpdateStatus(OrderBookStatus.PreOpen);

            book.CreateOrder(CompanyId1, OrderId1, OrderValidity.Day, Side.Buy, 3, 140);
            book.CreateOrder(CompanyId2, OrderId2, OrderValidity.Day, Side.Buy, 7, 130);
            book.CreateOrder(CompanyId3, OrderId3, OrderValidity.Day, Side.Buy, 15, 110);
            book.CreateOrder(CompanyId4, OrderId4, OrderValidity.Day, Side.Sell, 5, 100);
            book.CreateOrder(CompanyId5, OrderId5, OrderValidity.Day, Side.Sell, 6, 120);
            book.CreateOrder(CompanyId6, OrderId6, OrderValidity.Day, Side.Sell, 20, 140);

            // act
            var events = book.UpdateStatus(OrderBookStatus.Open);

            // assert
            var matched = events[1] as OrdersMatched;
            Assert.IsNotNull(matched);
            Assert.AreEqual(120, matched.Price);
        }

        [Test]
        public void Opening_TiedCandidates_ReferencePriceBreaksTieOverCmeDirectionRule()
        {
            // arrange - same tie as above (120 vs 130), but with a reference price seeded at 130
            // (must be tick-aligned to TickSize 10) - that should win over the default rule
            var security = new Security("GCZ6", SecurityType.Future, 10, 10);
            var book = new InMemoryOrderBook(security, TimeProvider);
            book.UpdateStatus(OrderBookStatus.PreOpen, 130);

            book.CreateOrder(CompanyId1, OrderId1, OrderValidity.Day, Side.Buy, 3, 140);
            book.CreateOrder(CompanyId2, OrderId2, OrderValidity.Day, Side.Buy, 7, 130);
            book.CreateOrder(CompanyId3, OrderId3, OrderValidity.Day, Side.Buy, 15, 110);
            book.CreateOrder(CompanyId4, OrderId4, OrderValidity.Day, Side.Sell, 5, 100);
            book.CreateOrder(CompanyId5, OrderId5, OrderValidity.Day, Side.Sell, 6, 120);
            book.CreateOrder(CompanyId6, OrderId6, OrderValidity.Day, Side.Sell, 20, 140);

            // act
            var events = book.UpdateStatus(OrderBookStatus.Open);

            // assert
            var matched = events[1] as OrdersMatched;
            Assert.IsNotNull(matched);
            Assert.AreEqual(130, matched.Price);
        }

        [Test]
        public void Opening_NoCrossingBook_NoAuctionPrint()
        {
            // arrange
            var security = new Security("GCZ6", SecurityType.Future, 10, 10);
            var book = new InMemoryOrderBook(security, TimeProvider);
            book.UpdateStatus(OrderBookStatus.PreOpen);
            book.CreateOrder(CompanyId1, OrderId1, OrderValidity.Day, Side.Buy, 5, 100);

            // act
            var events = book.UpdateStatus(OrderBookStatus.Open);

            // assert
            Assert.AreEqual(1, events.Count);
            Assert.IsInstanceOf<StatusChanged>(events[0]);
            var buyLevels = book.GetLevels(Side.Buy, 10);
            Assert.AreEqual(1, buyLevels.Count);
            Assert.AreEqual(5, buyLevels[0].Quantity);
        }

        [Test]
        public void TryGetIndicativeAuctionPrice_ReflectsLiveStateDuringPreOpen()
        {
            // arrange
            var security = new Security("GCZ6", SecurityType.Future, 10, 10);
            var book = new InMemoryOrderBook(security, TimeProvider);
            book.UpdateStatus(OrderBookStatus.PreOpen);

            // assert - nothing resting yet
            Assert.IsFalse(book.TryGetIndicativeAuctionPrice(out _, out _));

            // act - one-sided book, still no cross
            book.CreateOrder(CompanyId1, OrderId1, OrderValidity.Day, Side.Buy, 10, 100);
            Assert.IsFalse(book.TryGetIndicativeAuctionPrice(out _, out _));

            // act - a crossing sell arrives
            book.CreateOrder(CompanyId2, OrderId2, OrderValidity.Day, Side.Sell, 10, 100);
            Assert.IsTrue(book.TryGetIndicativeAuctionPrice(out var price, out var quantity));
            Assert.AreEqual(100, price);
            Assert.AreEqual(10, quantity);

            // act - cancelling the crossing sell removes the cross again
            book.CancelOrder(CompanyId2, CancelId1, OrderId2);
            Assert.IsFalse(book.TryGetIndicativeAuctionPrice(out _, out _));
        }

        [Test]
        public void RestingOrderOutsideBand_WithoutCrossingAnything_DoesNotPause()
        {
            // arrange - a resting limit order sitting far from the market doesn't represent any
            // actual price movement by itself; only an executed trade at an extreme price should
            // pause the book. This order doesn't cross anything (isolated at 200, nothing on the
            // opposing side), so it must not trigger a pause just for being entered.
            var security = new Security("GCZ6", SecurityType.Future, 10, 10, VolatilityAuctionBandTicks: 5);
            var book = new InMemoryOrderBook(security, TimeProvider);
            book.UpdateStatus(OrderBookStatus.Open, 100);

            // act
            var events = book.CreateOrder(CompanyId1, OrderId1, OrderValidity.Day, Side.Sell, 10, 200);

            // assert - accepted normally, no pause
            Assert.AreEqual(1, events.Count);
            Assert.IsInstanceOf<CreateOrderConfirmed>(events[0]);
            Assert.AreEqual(OrderBookStatus.Open, book.Status);
        }

        [Test]
        public void VolatilityBandBreach_OnActualTrade_PausesInsteadOfExecuting_StopElectsOnReopen()
        {
            // arrange - continuous trading establishes a reference price of 100, then a resting
            // sell stop (trigger 90) is added, plus a resting sell at 200 that hasn't crossed
            // anything yet (so hasn't paused anything, per the test above)
            var security = new Security("GCZ6", SecurityType.Future, 10, 10, VolatilityAuctionBandTicks: 5);
            var book = new InMemoryOrderBook(security, TimeProvider);
            book.UpdateStatus(OrderBookStatus.Open);
            book.CreateOrder(CompanyId1, OrderId1, OrderValidity.Day, Side.Buy, 5, 100);
            book.CreateOrder(CompanyId2, OrderId2, OrderValidity.Day, Side.Sell, 5, 100);
            TimeProvider.SetCurrentTime(Now2);
            book.CreateOrder(CompanyId3, OrderId3, OrderValidity.Day, Side.Sell, 5, 80, 90);
            book.CreateOrder(CompanyId4, OrderId4, OrderValidity.Day, Side.Sell, 10, 200);

            // act - a buy at 200 would actually cross and trade against the resting 200 sell, at a
            // price 100 away from the 100 reference - breaching the 50-wide volatility band. The
            // trade is prevented (not executed) and the book pauses instead; neither order fills
            TimeProvider.SetCurrentTime(Now3);
            var events = book.CreateOrder(CompanyId5, OrderId5, OrderValidity.Day, Side.Buy, 10, 200);

            // assert
            Assert.AreEqual(2, events.Count);
            Assert.IsInstanceOf<CreateOrderConfirmed>(events[0]);
            Assert.AreEqual(OrderBookStatus.PreOpen, ((StatusChanged) events[1]).Status);
            Assert.AreEqual(OrderBookStatus.PreOpen, book.Status);

            var sellLevels = book.GetLevels(Side.Sell, 10);
            Assert.AreEqual(1, sellLevels.Count);
            Assert.AreEqual(200, sellLevels[0].Price);
            Assert.AreEqual(10, sellLevels[0].Quantity); // untouched - the trade was prevented

            // orders can still be entered/cancelled while paused
            TimeProvider.SetCurrentTime(Now4);
            book.CreateOrder(CompanyId1, OrderId7, OrderValidity.Day, Side.Buy, 10, 90);
            book.CreateOrder(CompanyId2, OrderId8, OrderValidity.Day, Side.Sell, 10, 90);

            // act - ending the pause (seeding a fresh reference of 90) runs the same uncrossing
            // pass, clearing at 90 and moving the last traded price there, which elects the
            // resting stop (trigger 90, satisfied by <= 90)
            var reopenEvents = book.UpdateStatus(OrderBookStatus.Open, 90);

            // assert
            Assert.IsTrue(reopenEvents.OfType<OrdersMatched>().Any(m => m.Price == 90));
            var stopElected = reopenEvents.Any(e => e is UpdateOrderConfirmed u && u.Order.ClientOrderId == OrderId3) ||
                reopenEvents.OfType<OrdersMatched>().Any(m => m.Fills.Any(f => f.ClientOrderId == OrderId3));
            Assert.IsTrue(stopElected);
        }

        [Test]
        public void PriceBandTicksOnly_NoVolatilityAuctionBandConfigured_Unaffected()
        {
            // arrange - #23's hard-reject band still works exactly as before when
            // VolatilityAuctionBandTicks isn't set
            var security = new Security("GCZ6", SecurityType.Future, 10, 10, PriceBandTicks: 5);
            var book = new InMemoryOrderBook(security, TimeProvider);
            book.UpdateStatus(OrderBookStatus.Open, 100);

            // act
            var events = book.CreateOrder(CompanyId1, OrderId1, OrderValidity.Day, Side.Buy, 5, 200);

            // assert - rejected outright, no pause
            var rejected = events[0] as CreateOrderRejected;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(OrderRejectedReason.PriceOutsideBands, rejected.Reason);
            Assert.AreEqual(OrderBookStatus.Open, book.Status);
        }
    }
}
