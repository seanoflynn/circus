using System;
using Circus.OrderBook;
using NUnit.Framework;

namespace Circus.Tests.OrderBook
{
    [TestFixture]
    public class VolatilityBandRestrictionTests
    {
        [Test]
        public void Scope_IsTrade_OnBreach_IsPause()
        {
            var restriction = new VolatilityBandRestriction(5);

            Assert.AreEqual(RestrictionScope.Trade, restriction.Scope);
            Assert.AreEqual(RestrictionBreachAction.Pause, restriction.OnBreach);
        }

        [Test]
        public void NoDurationConfigured_PauseIsOpenEnded()
        {
            var restriction = new VolatilityBandRestriction(5);

            Assert.IsNull(restriction.ResumeAfter);
        }

        [Test]
        public void DurationConfigured_IsWhatThePauseResumesAfter()
        {
            var restriction = new VolatilityBandRestriction(5, TimeSpan.FromMinutes(2));

            Assert.AreEqual(TimeSpan.FromMinutes(2), restriction.ResumeAfter);
        }

        [Test]
        public void NoReferencePriceYet_AlwaysAllows()
        {
            var restriction = new VolatilityBandRestriction(5);

            Assert.IsTrue(restriction.Allows(1_000_000, default));
        }

        [Test]
        public void WithinBand_Allowed_AtEdge_Allowed_BeyondEdge_Disallowed()
        {
            var restriction = new VolatilityBandRestriction(5);
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
            var restriction = new VolatilityBandRestriction(5);
            restriction.OnSessionChange(100);

            restriction.OnTrade(200, default);

            Assert.IsTrue(restriction.Allows(205, default));
            Assert.IsFalse(restriction.Allows(206, default));
            Assert.IsFalse(restriction.Allows(100, default));
        }
    }
}
