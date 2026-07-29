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
        public void NoDurationConfigured_PauseIsOpenEnded()
        {
            var restriction = new DailyPriceBandLimit(5);

            Assert.IsNull(restriction.ResumeAfter);
        }

        [Test]
        public void DurationConfigured_IsWhatThePauseResumesAfter()
        {
            var restriction = new DailyPriceBandLimit(5, System.TimeSpan.FromMinutes(2));

            Assert.AreEqual(System.TimeSpan.FromMinutes(2), restriction.ResumeAfter);
        }

        [Test]
        public void NoBandConfigured_AlwaysAllows()
        {
            var restriction = new DailyPriceBandLimit(null);
            restriction.OnSessionChange(100);

            Assert.IsTrue(restriction.Allows(1_000_000, default));
        }

        [Test]
        public void BandConfigured_NoReferencePriceYet_AlwaysAllows()
        {
            var restriction = new DailyPriceBandLimit(5);

            Assert.IsTrue(restriction.Allows(1_000_000, default));
        }

        [Test]
        public void WithinBand_Allowed_AtEdge_Allowed_BeyondEdge_Disallowed()
        {
            var restriction = new DailyPriceBandLimit(5);
            restriction.OnSessionChange(100);

            Assert.IsTrue(restriction.Allows(100, default));
            Assert.IsTrue(restriction.Allows(105, default));
            Assert.IsTrue(restriction.Allows(95, default));
            Assert.IsFalse(restriction.Allows(106, default));
            Assert.IsFalse(restriction.Allows(94, default));
        }

        [Test]
        public void OnTrade_MovesReferenceToLastTrade()
        {
            var restriction = new DailyPriceBandLimit(5);
            restriction.OnSessionChange(100);

            restriction.OnTrade(200, default);

            Assert.IsTrue(restriction.Allows(205, default));
            Assert.IsFalse(restriction.Allows(206, default));
            Assert.IsFalse(restriction.Allows(100, default));
        }
    }
}
