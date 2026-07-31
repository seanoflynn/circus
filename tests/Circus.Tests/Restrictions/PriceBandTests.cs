using Circus.Actions;
using Circus.Events;
using Circus.Tests.Helpers;
using Circus.Time;
using NUnit.Framework;

namespace Circus.Tests.Restrictions;

[TestFixture]
public class PriceBandTests
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

    private static ManualClock Clock;

    [SetUp]
    public void SetUp()
    {
        Clock = new ManualClock(Now1);
    }

    [Test]
    public void NoBandConfigured_OrderFarFromReferencePrice_Accepted()
    {
        // arrange - no restrictions at all, so banding is off even though a reference price is
        // seeded. A security without a band leaves the restriction out rather than configuring
        // one with no width.
        var security = new Instrument("GCZ6", 10, 10);
        var book = new LevelTrackingOrderBook(security, Clock);
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
        var security = new Instrument("GCZ6", 10, 10,
            PriceRestrictions: new PriceRestrictionConfig[]
            {
                new OrderPriceBand(1000),
                new VolatilityBand(5)
            });
        var book = new LevelTrackingOrderBook(security, Clock);
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
        var security = new Instrument("GCZ6", 10, 10,
            PriceRestrictions: new PriceRestrictionConfig[] {new OrderPriceBand(5)});
        var book = new LevelTrackingOrderBook(security, Clock);
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
        var security = new Instrument("GCZ6", 10, 10,
            PriceRestrictions: new PriceRestrictionConfig[] {new OrderPriceBand(5)});
        var book = new LevelTrackingOrderBook(security, Clock);
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
        var security = new Instrument("GCZ6", 10, 10,
            PriceRestrictions: new PriceRestrictionConfig[] {new OrderPriceBand(5)});
        var book = new LevelTrackingOrderBook(security, Clock);
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
        var security = new Instrument("GCZ6", 10, 10,
            PriceRestrictions: new PriceRestrictionConfig[] {new OrderPriceBand(5)});
        var book = new LevelTrackingOrderBook(security, Clock);
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

    // A band of 5 ticks on a tick size of 10 is 50 either side of wherever the reference is.
    private static Instrument BandedSecurity() =>
        new("GCZ6", 10, 10,
            PriceRestrictions: new PriceRestrictionConfig[] {new OrderPriceBand(5)});

    [Test]
    public void PreOpen_ReferenceMovesFromTheSettlementPriceToTheIndicativePrice()
    {
        // arrange - settled at 100, so the band starts out [50, 150]
        var book = new LevelTrackingOrderBook(BandedSecurity(), Clock);
        book.UpdateStatus(OrderBookStatus.PreOpen, 100);

        // assert - 200 is outside the band while the settlement price is still the reference
        var beforeCross = book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 5, 200);
        Assert.AreEqual(OrderRejectedReason.PriceOutsideBands, (beforeCross[0] as CreateOrderRejected)?.Reason);

        // act - the book crosses at 150, so an indicative price exists and takes over as the
        // reference, moving the band to [100, 200]
        book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Buy, 10, 150);
        var crossing = book.CreateLimitOrder(CompanyId3, OrderId3, new OrderValidity.Day(), Side.Sell, 10, 150);
        Assert.AreEqual(150, crossing.OfType<IndicativePriceChanged>().Last().Price);

        // assert - the same price that was rejected a moment ago is now inside the band
        var afterCross = book.CreateLimitOrder(CompanyId4, OrderId4, new OrderValidity.Day(), Side.Buy, 5, 200);
        Assert.IsInstanceOf<CreateOrderConfirmed>(afterCross[0]);

        // ...and 90, which the old band allowed, is now outside it
        var belowNewBand = book.CreateLimitOrder(CompanyId4, "Order5", new OrderValidity.Day(), Side.Buy, 5, 90);
        Assert.AreEqual(OrderRejectedReason.PriceOutsideBands, (belowNewBand[0] as CreateOrderRejected)?.Reason);
    }

    [Test]
    public void ContinuousTrading_ReferenceReturnsToTheLastTrade_OnceTheQuoteIsWithdrawn()
    {
        // arrange - open on an auction that prints at 150, which withdraws the quote
        var book = new LevelTrackingOrderBook(BandedSecurity(), Clock);
        book.UpdateStatus(OrderBookStatus.PreOpen, 100);
        book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 10, 150);
        book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Sell, 10, 150);
        book.UpdateStatus(OrderBookStatus.Open);

        // act - continuous trading prints at 190, which the band [100, 200] allows
        book.CreateLimitOrder(CompanyId3, OrderId3, new OrderValidity.Day(), Side.Buy, 5, 190);
        book.CreateLimitOrder(CompanyId4, OrderId4, new OrderValidity.Day(), Side.Sell, 5, 190);

        // assert - the band tracks the last trade, so it is now [140, 240]. Were the withdrawn
        // indicative price of 150 still the reference, 240 would be well outside it.
        var atNewEdge = book.CreateLimitOrder(CompanyId1, "Order5", new OrderValidity.Day(), Side.Buy, 5, 240);
        Assert.IsInstanceOf<CreateOrderConfirmed>(atNewEdge[0]);

        var belowNewBand = book.CreateLimitOrder(CompanyId1, "Order6", new OrderValidity.Day(), Side.Buy, 5, 130);
        Assert.AreEqual(OrderRejectedReason.PriceOutsideBands, (belowNewBand[0] as CreateOrderRejected)?.Reason);
    }

    // Trades at 100 to give stop orders a last traded price, then re-seeds the reference at 200
    // so the band sits somewhere the last traded price does not. Without that separation the
    // spread rule cannot be isolated: with the band centred on the last trade, a trigger on the
    // far side of it can never be more than a band away from a limit price still inside it.
    private LevelTrackingOrderBook StopSpreadBook(Instrument instrument)
    {
        var book = new LevelTrackingOrderBook(instrument, Clock);
        book.UpdateStatus(OrderBookStatus.Open, 100);
        book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 5, 100);
        book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Sell, 5, 100);
        book.UpdateStatus(OrderBookStatus.Open, 200);
        return book;
    }

    [Test]
    public void StopLimit_TriggerAndPriceWithinABandOfEachOther_Accepted()
    {
        // arrange - band [150, 250] around the re-seeded reference of 200
        var book = StopSpreadBook(BandedSecurity());

        // act - trigger 160 and limit 210 are 50 apart, exactly the band width
        var events = book.CreateStopLimitOrder(CompanyId3, OrderId3, new OrderValidity.Day(),
            Side.Buy, 5, 210, 160);

        // assert
        Assert.IsInstanceOf<CreateOrderConfirmed>(events[0]);
    }

    [Test]
    public void StopLimit_TriggerAndPriceFurtherApartThanTheBand_Rejected()
    {
        // arrange
        var book = StopSpreadBook(BandedSecurity());

        // act - trigger 160 and limit 250 are 90 apart, beyond the band
        var events = book.CreateStopLimitOrder(CompanyId3, OrderId3, new OrderValidity.Day(),
            Side.Buy, 5, 250, 160);

        // assert - and note the limit price of 250 sits exactly on the band's own edge, so the
        // entry band alone would have accepted this order. Only the spread rule catches it.
        var rejected = events[0] as CreateOrderRejected;
        Assert.IsNotNull(rejected);
        Assert.AreEqual(OrderRejectedReason.TriggerPriceTooFarFromPrice, rejected.Reason);
    }

    [Test]
    public void UpdateOrder_RepricingAStopBeyondTheBandFromItsTrigger_Rejected()
    {
        // arrange - an accepted stop, trigger 160 and limit 210
        var book = StopSpreadBook(BandedSecurity());
        book.CreateStopLimitOrder(CompanyId3, OrderId3, new OrderValidity.Day(), Side.Buy, 5, 210, 160);

        // act - repricing to 250 leaves the limit inside the band but 90 from the trigger
        var events = book.UpdateOrder(CompanyId3, OrderId4, OrderId3, price: 250);

        // assert
        var rejected = events[0] as UpdateOrderRejected;
        Assert.IsNotNull(rejected);
        Assert.AreEqual(OrderRejectedReason.TriggerPriceTooFarFromPrice, rejected.Reason);
    }

    [Test]
    public void NoBandConfigured_StopSpreadUnbounded()
    {
        // arrange - nothing to bound the gap with
        var book = StopSpreadBook(new Instrument("GCZ6", 10, 10));

        // act
        var events = book.CreateStopLimitOrder(CompanyId3, OrderId3, new OrderValidity.Day(),
            Side.Buy, 5, 1000, 160);

        // assert
        Assert.IsInstanceOf<CreateOrderConfirmed>(events[0]);
    }
}
