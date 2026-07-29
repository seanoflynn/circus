using Circus.OrderBook;
using NUnit.Framework;

namespace Circus.Tests.OrderBook;

[TestFixture]
public class OrderPriceRestrictionTests
{
    [Test]
    public void Scope_IsOrderEntry_OnBreach_IsReject()
    {
        var restriction = new OrderPriceRestriction(5);

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
    public void NoReferencePriceYet_AlwaysAllows()
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

    [Test]
    public void IndicativePrice_OutranksTheLastTrade()
    {
        // an auction is quoting, so that is what an order is judged against - CME's banding
        // reference follows the IOP once one exists
        var restriction = new OrderPriceRestriction(5);
        restriction.OnSessionChange(100);
        restriction.OnTrade(200, default);

        restriction.OnIndicativePrice(300);

        Assert.IsTrue(restriction.Allows(305, default));
        Assert.IsFalse(restriction.Allows(306, default));
        Assert.IsFalse(restriction.Allows(200, default), "the last trade no longer decides");
    }

    [Test]
    public void IndicativePrice_OutranksTheSessionReference_WithNoTradeYet()
    {
        // pre-open on a fresh session: settled at 100, then the book crosses at 300
        var restriction = new OrderPriceRestriction(5);
        restriction.OnSessionChange(100);

        restriction.OnIndicativePrice(300);

        Assert.IsTrue(restriction.Allows(300, default));
        Assert.IsFalse(restriction.Allows(100, default));
    }

    [Test]
    public void IndicativePriceWithdrawn_FallsBackToTheLastTrade()
    {
        // the auction ends and continuous trading takes over, which quotes nothing
        var restriction = new OrderPriceRestriction(5);
        restriction.OnSessionChange(100);
        restriction.OnTrade(200, default);
        restriction.OnIndicativePrice(300);

        restriction.OnIndicativePrice(null);

        Assert.IsTrue(restriction.Allows(205, default));
        Assert.IsFalse(restriction.Allows(300, default));
    }

    [Test]
    public void IndicativePriceWithdrawn_NoTradeYet_FallsBackToTheSessionReference()
    {
        var restriction = new OrderPriceRestriction(5);
        restriction.OnSessionChange(100);
        restriction.OnIndicativePrice(300);

        restriction.OnIndicativePrice(null);

        Assert.IsTrue(restriction.Allows(105, default));
        Assert.IsFalse(restriction.Allows(300, default));
    }

    [Test]
    public void OnSessionChange_ClearsWhatItSupersedes()
    {
        // a new session's settlement price has to beat the previous session's last trade, so
        // an explicit reference clears the anchors above it rather than sitting underneath them
        var restriction = new OrderPriceRestriction(5);
        restriction.OnTrade(200, default);
        restriction.OnIndicativePrice(300);

        restriction.OnSessionChange(100);

        Assert.IsTrue(restriction.Allows(105, default));
        Assert.IsFalse(restriction.Allows(200, default));
        Assert.IsFalse(restriction.Allows(300, default));
    }

    [Test]
    public void AllowsStopSpread_WithinWidth_AtWidth_BeyondWidth()
    {
        var restriction = new OrderPriceRestriction(5);

        Assert.IsTrue(restriction.AllowsStopSpread(0));
        Assert.IsTrue(restriction.AllowsStopSpread(4));
        Assert.IsTrue(restriction.AllowsStopSpread(5));
        Assert.IsFalse(restriction.AllowsStopSpread(6));
    }

    [Test]
    public void AllowsStopSpread_DoesNotDependOnAReference()
    {
        // it measures a width, not a distance from anywhere
        var restriction = new OrderPriceRestriction(5);

        Assert.IsTrue(restriction.AllowsStopSpread(5));
        Assert.IsFalse(restriction.AllowsStopSpread(6));
    }
}
