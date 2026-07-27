using Circus.OrderBook;
using NUnit.Framework;

namespace Circus.Tests.OrderBook
{
    [TestFixture]
    public class DailyPriceBandLimitTests
    {
        [Test]
        public void Scope_IsTrade_OnBreach_IsPause()
        {
            var restriction = new DailyPriceBandLimit(new Security("GCZ6", SecurityType.Future, 10, 10));

            Assert.AreEqual(RestrictionScope.Trade, restriction.Scope);
            Assert.AreEqual(RestrictionBreachAction.Pause, restriction.OnBreach);
        }

        [Test]
        public void NoBandConfigured_AlwaysAllows()
        {
            var restriction = new DailyPriceBandLimit(new Security("GCZ6", SecurityType.Future, 10, 10));
            restriction.OnSessionChange(100);

            Assert.IsTrue(restriction.Allows(1_000_000));
        }

        [Test]
        public void BandConfigured_NoReferencePriceYet_AlwaysAllows()
        {
            var security = new Security("GCZ6", SecurityType.Future, 10, 10, VolatilityAuctionBandTicks: 5);
            var restriction = new DailyPriceBandLimit(security);

            Assert.IsTrue(restriction.Allows(1_000_000));
        }

        [Test]
        public void WithinBand_Allowed_AtEdge_Allowed_BeyondEdge_Disallowed()
        {
            var security = new Security("GCZ6", SecurityType.Future, 10, 10, VolatilityAuctionBandTicks: 5);
            var restriction = new DailyPriceBandLimit(security);
            restriction.OnSessionChange(100);

            Assert.IsTrue(restriction.Allows(100));
            Assert.IsTrue(restriction.Allows(105));
            Assert.IsTrue(restriction.Allows(95));
            Assert.IsFalse(restriction.Allows(106));
            Assert.IsFalse(restriction.Allows(94));
        }

        [Test]
        public void IndependentOfPriceBandTicks_OnlyReadsVolatilityAuctionBandTicks()
        {
            // PriceBandTicks configured wide, VolatilityAuctionBandTicks configured narrow -
            // the two restrictions must not read each other's threshold.
            var security = new Security("GCZ6", SecurityType.Future, 10, 10, PriceBandTicks: 1000,
                VolatilityAuctionBandTicks: 5);
            var restriction = new DailyPriceBandLimit(security);
            restriction.OnSessionChange(100);

            Assert.IsFalse(restriction.Allows(200));
        }

        [Test]
        public void OnTrade_MovesReferenceToLastTrade()
        {
            var security = new Security("GCZ6", SecurityType.Future, 10, 10, VolatilityAuctionBandTicks: 5);
            var restriction = new DailyPriceBandLimit(security);
            restriction.OnSessionChange(100);

            restriction.OnTrade(200, default);

            Assert.IsTrue(restriction.Allows(205));
            Assert.IsFalse(restriction.Allows(206));
            Assert.IsFalse(restriction.Allows(100));
        }
    }
}
