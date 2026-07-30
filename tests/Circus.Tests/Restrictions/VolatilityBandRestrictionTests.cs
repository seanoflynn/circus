using Circus.Restrictions;
using NUnit.Framework;

namespace Circus.Tests.Restrictions;

[TestFixture]
public class VolatilityBandRestrictionTests
{
    private static readonly DateTime T = new(2000, 1, 1, 12, 0, 0);

    [Test]
    public void Scope_IsTrade_OnBreach_IsPause()
    {
        var restriction = new VolatilityBandRestriction(5);

        Assert.AreEqual(RestrictionScope.Trade, restriction.Scope);
        Assert.AreEqual(RestrictionBreachAction.Pause, restriction.OnBreach);
    }

    [Test]
    public void Window_MeasuresAgainstEveryTradeStillInIt()
    {
        // arrange - two trades 10 seconds apart, both inside a 30-second window
        var restriction = new VolatilityBandRestriction(5, window: TimeSpan.FromSeconds(30));
        restriction.OnTrade(100, T);
        restriction.OnTrade(103, T.AddSeconds(10));

        // assert - 107 is within range of the newer trade but not of the older one, and the
        // older one still counts
        Assert.IsTrue(restriction.Allows(105, T.AddSeconds(11)), "in range of both");
        Assert.IsFalse(restriction.Allows(107, T.AddSeconds(11)), "in range of 103 but not of 100");
    }

    [Test]
    public void Window_TradesAgeOutOfIt()
    {
        // arrange - the same pair
        var restriction = new VolatilityBandRestriction(5, window: TimeSpan.FromSeconds(30));
        restriction.OnTrade(100, T);
        restriction.OnTrade(103, T.AddSeconds(10));

        // act - far enough on that only the newer trade is still inside the window
        var laterOn = T.AddSeconds(31);

        // assert - the price refused a moment ago is allowed now, purely because time passed
        Assert.IsTrue(restriction.Allows(107, laterOn));
    }

    [Test]
    public void Window_NeverEmptiesCompletely()
    {
        // arrange - a market that has gone quiet is still measured against where it last
        // traded, not against nothing at all
        var restriction = new VolatilityBandRestriction(5, window: TimeSpan.FromSeconds(30));
        restriction.OnTrade(100, T);

        // assert
        Assert.IsTrue(restriction.Allows(105, T.AddDays(1)));
        Assert.IsFalse(restriction.Allows(106, T.AddDays(1)));
    }

    [Test]
    public void NoWindow_MeasuresAgainstTheLastTradeAlone()
    {
        // arrange - the older trade is discarded outright rather than kept and aged
        var restriction = new VolatilityBandRestriction(5);
        restriction.OnTrade(100, T);
        restriction.OnTrade(103, T.AddSeconds(10));

        // assert - 107 is out of range of 100, which no longer counts for anything
        Assert.IsTrue(restriction.Allows(107, T.AddSeconds(11)));
    }

    [Test]
    public void OnSessionChange_ClearsTheWindow()
    {
        // arrange - an explicit reference supersedes what the market did before it
        var restriction = new VolatilityBandRestriction(5, window: TimeSpan.FromSeconds(30));
        restriction.OnTrade(200, T);

        restriction.OnSessionChange(100);

        // assert - measured from the new reference, with the trade at 200 forgotten
        Assert.IsTrue(restriction.Allows(105, T.AddSeconds(1)));
        Assert.IsFalse(restriction.Allows(200, T.AddSeconds(1)));
    }

    [Test]
    public void NoExtendedRange_AnInterruptionAlwaysEnds()
    {
        var restriction = new VolatilityBandRestriction(5);
        restriction.OnTrade(100, T);

        Assert.IsFalse(restriction.Allows(1_000, T), "well outside the ordinary range");
        Assert.IsTrue(restriction.AllowsResumption(1_000, T), "but nothing holds the resumption back");
    }

    [Test]
    public void ExtendedRange_HoldsTheClosingPriceToAWiderRange()
    {
        // arrange - ordinary range 5, extended range 10
        var restriction = new VolatilityBandRestriction(5, extendedRangeTicks: 10);
        restriction.OnTrade(100, T);

        // assert - between the two ranges: enough to have caused the interruption, not enough
        // to keep it running
        Assert.IsFalse(restriction.Allows(106, T));
        Assert.IsTrue(restriction.AllowsResumption(106, T));

        // beyond the extended range: the interruption keeps running
        Assert.IsFalse(restriction.AllowsResumption(111, T));
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
    public void IndicativePrice_Ignored()
    {
        // volatility is measured against prices that actually traded, not one an auction is
        // only quoting - the entry band is the restriction that follows the indicative price
        var restriction = new VolatilityBandRestriction(5);
        restriction.OnSessionChange(100);

        restriction.OnIndicativePrice(300);

        Assert.IsTrue(restriction.Allows(105, default));
        Assert.IsFalse(restriction.Allows(300, default));
    }

    [Test]
    public void StopSpread_Unconstrained_NotAnEntryRestriction()
    {
        var restriction = new VolatilityBandRestriction(5);

        Assert.IsTrue(restriction.AllowsStopSpread(1_000_000));
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
