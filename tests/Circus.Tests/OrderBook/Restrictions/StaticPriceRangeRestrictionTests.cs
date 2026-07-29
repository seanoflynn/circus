using Circus.OrderBook.Restrictions;
using NUnit.Framework;

namespace Circus.Tests.OrderBook.Restrictions;

[TestFixture]
public class StaticPriceRangeRestrictionTests
{
    private static readonly DateTime T = new(2000, 1, 1, 12, 0, 0);

    [Test]
    public void Scope_IsTrade_OnBreach_IsPause()
    {
        var restriction = new StaticPriceRangeRestriction(5);

        Assert.AreEqual(RestrictionScope.Trade, restriction.Scope);
        Assert.AreEqual(RestrictionBreachAction.Pause, restriction.OnBreach);
    }

    [Test]
    public void NoReferenceYet_AlwaysAllows()
    {
        var restriction = new StaticPriceRangeRestriction(5);

        Assert.IsTrue(restriction.Allows(1_000_000, T));
    }

    [Test]
    public void WithinRange_Allowed_AtEdge_Allowed_BeyondEdge_Disallowed()
    {
        var restriction = new StaticPriceRangeRestriction(5);
        restriction.OnSessionChange(100);

        Assert.IsTrue(restriction.Allows(105, T));
        Assert.IsTrue(restriction.Allows(95, T));
        Assert.IsFalse(restriction.Allows(106, T));
        Assert.IsFalse(restriction.Allows(94, T));
    }

    [Test]
    public void TradesDoNotMoveIt()
    {
        // the whole point: a range that followed the market could be walked anywhere over a day
        // without ever being breached, because every step is small next to the last one
        var restriction = new StaticPriceRangeRestriction(5);
        restriction.OnSessionChange(100);

        restriction.OnTrade(105, T);
        restriction.OnTrade(110, T.AddSeconds(1));

        Assert.IsFalse(restriction.Allows(111, T.AddSeconds(2)),
            "still measured from 100, however far the market has walked");
    }

    [Test]
    public void IndicativePrice_Ignored()
    {
        var restriction = new StaticPriceRangeRestriction(5);
        restriction.OnSessionChange(100);

        restriction.OnIndicativePrice(300);

        Assert.IsFalse(restriction.Allows(300, T));
    }

    [Test]
    public void HasNoOpinionOnResumptionOrStopSpreads()
    {
        var restriction = new StaticPriceRangeRestriction(5);
        restriction.OnSessionChange(100);

        Assert.IsTrue(restriction.AllowsResumption(1_000_000, T));
        Assert.IsTrue(restriction.AllowsStopSpread(1_000_000));
    }

    [Test]
    public void DurationConfigured_IsWhatThePauseResumesAfter()
    {
        var restriction = new StaticPriceRangeRestriction(5, TimeSpan.FromMinutes(2));

        Assert.AreEqual(TimeSpan.FromMinutes(2), restriction.ResumeAfter);
    }
}
