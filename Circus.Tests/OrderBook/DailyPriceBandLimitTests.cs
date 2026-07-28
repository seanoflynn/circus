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
            var restriction = new DailyPriceBandLimit(null);

            Assert.AreEqual(RestrictionScope.Trade, restriction.Scope);
            Assert.AreEqual(RestrictionBreachAction.Pause, restriction.OnBreach);
        }

        [Test]
        public void NoBandConfigured_AlwaysAllows()
        {
            var restriction = new DailyPriceBandLimit(null);
            restriction.OnSessionChange(100);

            Assert.IsTrue(restriction.Allows(1_000_000));
        }

        [Test]
        public void BandConfigured_NoReferencePriceYet_AlwaysAllows()
        {
            var restriction = new DailyPriceBandLimit(5);

            Assert.IsTrue(restriction.Allows(1_000_000));
        }

        [Test]
        public void WithinBand_Allowed_AtEdge_Allowed_BeyondEdge_Disallowed()
        {
            var restriction = new DailyPriceBandLimit(5);
            restriction.OnSessionChange(100);

            Assert.IsTrue(restriction.Allows(100));
            Assert.IsTrue(restriction.Allows(105));
            Assert.IsTrue(restriction.Allows(95));
            Assert.IsFalse(restriction.Allows(106));
            Assert.IsFalse(restriction.Allows(94));
        }

        [Test]
        public void OnTrade_MovesReferenceToLastTrade()
        {
            var restriction = new DailyPriceBandLimit(5);
            restriction.OnSessionChange(100);

            restriction.OnTrade(200, default);

            Assert.IsTrue(restriction.Allows(205));
            Assert.IsFalse(restriction.Allows(206));
            Assert.IsFalse(restriction.Allows(100));
        }
    }
}
