using Circus.OrderBook;
using NUnit.Framework;

namespace Circus.Tests.OrderBook
{
    [TestFixture]
    public class OrderPriceRestrictionTests
    {
        [Test]
        public void Scope_IsOrderEntry_OnBreach_IsReject()
        {
            var restriction = new OrderPriceRestriction(null);

            Assert.AreEqual(RestrictionScope.OrderEntry, restriction.Scope);
            Assert.AreEqual(RestrictionBreachAction.Reject, restriction.OnBreach);
        }

        [Test]
        public void RejectionInterruptsNothing_SoThereIsNothingToResumeFrom()
        {
            var restriction = new OrderPriceRestriction(5);

            Assert.IsNull(restriction.ResumeAfter);
        }

        [Test]
        public void NoBandConfigured_AlwaysAllows()
        {
            var restriction = new OrderPriceRestriction(null);
            restriction.OnSessionChange(100);

            Assert.IsTrue(restriction.Allows(1_000_000, default));
        }

        [Test]
        public void BandConfigured_NoReferencePriceYet_AlwaysAllows()
        {
            var restriction = new OrderPriceRestriction(5);

            Assert.IsTrue(restriction.Allows(1_000_000, default));
        }

        [Test]
        public void WithinBand_Allowed_AtEdge_Allowed_BeyondEdge_Disallowed()
        {
            var restriction = new OrderPriceRestriction(5);
            restriction.OnSessionChange(100);

            Assert.IsTrue(restriction.Allows(100, default));
            Assert.IsTrue(restriction.Allows(105, default));
            Assert.IsTrue(restriction.Allows(95, default));
            Assert.IsFalse(restriction.Allows(106, default));
            Assert.IsFalse(restriction.Allows(94, default));
        }

        [Test]
        public void OnSessionChange_Null_DoesNotClearExistingReference()
        {
            var restriction = new OrderPriceRestriction(5);
            restriction.OnSessionChange(100);
            restriction.OnSessionChange(null);

            Assert.IsTrue(restriction.Allows(105, default));
            Assert.IsFalse(restriction.Allows(106, default));
        }

        [Test]
        public void OnTrade_MovesReferenceToLastTrade()
        {
            var restriction = new OrderPriceRestriction(5);
            restriction.OnSessionChange(100);

            restriction.OnTrade(200, default);

            // band now tracks the last trade (200), no longer the seed (100)
            Assert.IsTrue(restriction.Allows(205, default));
            Assert.IsFalse(restriction.Allows(206, default));
            Assert.IsFalse(restriction.Allows(100, default));
        }
    }
}
