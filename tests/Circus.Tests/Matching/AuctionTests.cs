using Circus.Actions;
using Circus.Events;
using Circus.Tests.Helpers;
using Circus.Time;
using NUnit.Framework;

namespace Circus.Tests.Matching;

[TestFixture]
public class AuctionTests
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

    private static ManualClock Clock;

    [SetUp]
    public void SetUp()
    {
        Clock = new ManualClock(Now1);
    }

    [Test]
    public void Opening_PicksMaxVolumePrice_AcrossMultipleLevels_WithPriceImprovement()
    {
        // arrange - best bid 140 vs best offer 110 would only cross 5 at the touch, but the
        // volume-maximizing single price is 120, clearing 11: the 140 and 130 buys fully fill
        // (price improvement - they pay 120, not their own higher limit), the 110 and 120
        // sells fully fill (also price improvement - they get 120, not their own lower limit),
        // and the 120 buy (the marginal order) only partially fills for the 1 unit left over
        var security = new Instrument("GCZ6", 10, 10);
        var book = new LevelTrackingOrderBook(security, Clock);
        book.UpdateStatus(OrderBookStatus.PreOpen);

        book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 3, 140);
        book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Buy, 7, 130);
        book.CreateLimitOrder(CompanyId3, OrderId3, new OrderValidity.Day(), Side.Buy, 15, 120);
        book.CreateLimitOrder(CompanyId4, OrderId4, new OrderValidity.Day(), Side.Sell, 5, 110);
        book.CreateLimitOrder(CompanyId5, OrderId5, new OrderValidity.Day(), Side.Sell, 6, 120);
        book.CreateLimitOrder(CompanyId6, OrderId6, new OrderValidity.Day(), Side.Sell, 20, 130);

        // act
        var events = book.UpdateStatus(OrderBookStatus.Open);

        // assert - every fill in this batch prints at the single auction price of 120
        Assert.IsInstanceOf<StatusChanged>(events[0]);
        var matches = events.OfType<OrdersMatched>().ToList();
        Assert.IsNotEmpty(matches);
        foreach (var matched in matches)
            Assert.AreEqual(120, matched.Price);

        Assert.AreEqual(11, matches.Sum(m => m.Quantity));

        // the auction it was quoting is over, so the quote is withdrawn
        Assert.IsNull(events.OfType<IndicativePriceChanged>().Last().Price);

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
    public void Opening_IcebergAheadOfLaterOrder_FillsInFullBeforeTheLaterOrderGetsAnything()
    {
        // arrange - an iceberg (qty 100, peak 10) rests first at the clearing price; a plain
        // order (qty 60) arrives second at the same price. Only 100 total sell liquidity is
        // available - exactly enough to satisfy the iceberg's full size and nothing else.
        // Price-time priority means the iceberg, having arrived first, must be filled in full
        // before the later order gets anything - it must not lose its place in the queue just
        // because its displayed peak needs replenishing mid-print.
        var security = new Instrument("GCZ6", 10, 10);
        var book = new LevelTrackingOrderBook(security, Clock);
        book.UpdateStatus(OrderBookStatus.PreOpen);

        book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 100, 100,
            maxVisibleQuantity: 10);
        Clock.SetCurrentTime(Now2);
        book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Buy, 60, 100);
        Clock.SetCurrentTime(Now3);
        book.CreateLimitOrder(CompanyId3, OrderId3, new OrderValidity.Day(), Side.Sell, 100, 100);

        // act
        var events = book.UpdateStatus(OrderBookStatus.Open);

        // assert
        var fills = events.OfType<OrdersMatched>().SelectMany(m => m.Fills).ToList();
        var icebergFilled = fills.Where(f => f.Order.ClientOrderId == OrderId1).Sum(f => f.Quantity);
        var laterOrderFilled = fills.Where(f => f.Order.ClientOrderId == OrderId2).Sum(f => f.Quantity);

        Assert.AreEqual(100, icebergFilled, "the earlier-arriving iceberg should be filled in full");
        Assert.AreEqual(0, laterOrderFilled,
            "the later order should receive nothing once the earlier iceberg exhausts all available liquidity");
    }

    [Test]
    public void Opening_IcebergPartiallyFilled_DisplayedPeakCorrectAfterwardsNotNegative()
    {
        // arrange - an iceberg (qty 100, peak 10) is the only buy order at the clearing price;
        // only 45 units of sell liquidity are available, so it fills for 45 in a single
        // auction allocation (not five separate peak-sized touches) and is left resting with
        // 55 remaining. Sizing that single fill against the full remaining quantity rather
        // than the 10-unit peak must not leave DisplayedQuantity negative or stale - it should
        // come out re-derived to a fresh full peak, the same as if it had just been entered.
        var security = new Instrument("GCZ6", 10, 10);
        var book = new LevelTrackingOrderBook(security, Clock);
        book.UpdateStatus(OrderBookStatus.PreOpen);

        book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 100, 100,
            maxVisibleQuantity: 10);
        Clock.SetCurrentTime(Now2);
        book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Sell, 45, 100);

        // act
        var events = book.UpdateStatus(OrderBookStatus.Open);

        // assert - one clean allocation of 45, not several smaller touches
        var icebergFills = events.OfType<OrdersMatched>().SelectMany(m => m.Fills)
            .Where(f => f.Order.ClientOrderId == OrderId1).ToList();
        Assert.AreEqual(1, icebergFills.Count);
        Assert.AreEqual(45, icebergFills[0].Quantity);

        var buyLevels = book.GetLevels(Side.Buy, 10);
        Assert.AreEqual(1, buyLevels.Count);
        Assert.AreEqual(100, buyLevels[0].Price);
        Assert.AreEqual(10, buyLevels[0].Quantity, "displayed peak should be a fresh full 10, not negative or stale");
    }

    [Test]
    public void Opening_TiedCandidates_DefaultsToCmeDirectionRule()
    {
        // arrange - 120 and 130 tie for max executable volume (10 each), with the surplus on
        // the sell side at both - CME's rule picks the lowest of the tied prices in that case
        var security = new Instrument("GCZ6", 10, 10);
        var book = new LevelTrackingOrderBook(security, Clock);
        book.UpdateStatus(OrderBookStatus.PreOpen);

        book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 3, 140);
        book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Buy, 7, 130);
        book.CreateLimitOrder(CompanyId3, OrderId3, new OrderValidity.Day(), Side.Buy, 15, 110);
        book.CreateLimitOrder(CompanyId4, OrderId4, new OrderValidity.Day(), Side.Sell, 5, 100);
        book.CreateLimitOrder(CompanyId5, OrderId5, new OrderValidity.Day(), Side.Sell, 6, 120);
        book.CreateLimitOrder(CompanyId6, OrderId6, new OrderValidity.Day(), Side.Sell, 20, 140);

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
        var security = new Instrument("GCZ6", 10, 10);
        var book = new LevelTrackingOrderBook(security, Clock);
        book.UpdateStatus(OrderBookStatus.PreOpen, 130);

        book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 3, 140);
        book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Buy, 7, 130);
        book.CreateLimitOrder(CompanyId3, OrderId3, new OrderValidity.Day(), Side.Buy, 15, 110);
        book.CreateLimitOrder(CompanyId4, OrderId4, new OrderValidity.Day(), Side.Sell, 5, 100);
        book.CreateLimitOrder(CompanyId5, OrderId5, new OrderValidity.Day(), Side.Sell, 6, 120);
        book.CreateLimitOrder(CompanyId6, OrderId6, new OrderValidity.Day(), Side.Sell, 20, 140);

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
        var security = new Instrument("GCZ6", 10, 10);
        var book = new LevelTrackingOrderBook(security, Clock);
        book.UpdateStatus(OrderBookStatus.PreOpen);
        book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 5, 100);

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
    public void PreOpen_CrossedBook_QuotesButNeverTrades()
    {
        // arrange - pre-open is governed by an auction, so the one thing that must never
        // happen is that governance turning into execution: a crossed book during pre-open is
        // quoted, not printed. Orders sit untouched until the open.
        var security = new Instrument("GCZ6", 10, 10);
        var book = new LevelTrackingOrderBook(security, Clock);
        book.UpdateStatus(OrderBookStatus.PreOpen);

        // act - deeply crossed: the bid is well through the offer
        var buy = book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 10, 150);
        var sell = book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Sell, 10, 100);

        // assert - a confirmation each, and no trade
        Assert.AreEqual(1, buy.Count, "nothing crosses yet, so not even a quote");
        Assert.IsInstanceOf<CreateOrderConfirmed>(buy[0]);
        Assert.AreEqual(2, sell.Count);
        Assert.IsInstanceOf<CreateOrderConfirmed>(sell[0]);

        // the auction is quoting the whole time, it just isn't acting on it
        var quote = sell[1] as IndicativePriceChanged;
        Assert.IsNotNull(quote);
        Assert.AreEqual(100, quote.Price);
        Assert.AreEqual(10, quote.Quantity);

        // and both orders are still resting in full
        Assert.AreEqual(10, book.GetLevels(Side.Buy, 10)[0].Quantity);
        Assert.AreEqual(10, book.GetLevels(Side.Sell, 10)[0].Quantity);
    }

    [Test]
    public void ClosingFromPreOpen_AbandonsTheAuction_WithoutPrinting()
    {
        // arrange - a crossed book in pre-open, quoting a price it would clear at
        var security = new Instrument("GCZ6", 10, 10);
        var book = new LevelTrackingOrderBook(security, Clock);
        book.UpdateStatus(OrderBookStatus.PreOpen);
        book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 10, 150);
        var quoting = book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Sell, 10, 100);
        Assert.IsNotEmpty(quoting.OfType<IndicativePriceChanged>().ToList());

        // act - closing rather than opening. Pre-open is a phase that prints on the way out,
        // but the phase being entered doesn't trade, so the orders it accumulated are
        // abandoned rather than crossed.
        var events = book.UpdateStatus(OrderBookStatus.Closed);

        // assert
        Assert.IsEmpty(events.OfType<OrdersMatched>().ToList(),
            "the auction must not print into a phase that doesn't trade");
        Assert.AreEqual(2, events.OfType<ExpireOrderConfirmed>().ToList().Count);
        Assert.AreEqual(OrderBookStatus.Closed, book.Status);

        // the quote goes with it - a closed book is not still quoting one
        var quote = events.OfType<IndicativePriceChanged>().Last();
        Assert.IsNull(quote.Price);
        Assert.AreEqual(0, quote.Quantity);
    }

    [Test]
    public void IndicativePrice_NotQuotedWhileOpen()
    {
        // arrange - continuous trading is governed by price-time, which has no single price it
        // would print at, so there is no indicative auction price to publish while open
        var security = new Instrument("GCZ6", 10, 10);
        var book = new LevelTrackingOrderBook(security, Clock);
        book.UpdateStatus(OrderBookStatus.Open);

        // act + assert - a resting order, then one that crosses it and trades: neither quotes
        var resting = book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 10, 100);
        var crossing = book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Sell, 4, 100);

        Assert.IsEmpty(resting.OfType<IndicativePriceChanged>().ToList());
        Assert.IsInstanceOf<OrdersMatched>(crossing[^1]);
        Assert.IsEmpty(crossing.OfType<IndicativePriceChanged>().ToList());

        // ...and it comes back the moment a volatility pause interrupts trading
        book.UpdateStatus(OrderBookStatus.Paused);
        var paused = book.CreateLimitOrder(CompanyId3, OrderId3, new OrderValidity.Day(), Side.Sell, 10, 100);

        var quote = paused.OfType<IndicativePriceChanged>().Last();
        Assert.AreEqual(100, quote.Price);
        Assert.AreEqual(6, quote.Quantity, "what is left of the buy after the trade above");
    }

    [Test]
    public void IndicativePrice_PublishedAsThePreOpenBookMoves()
    {
        // arrange
        var security = new Instrument("GCZ6", 10, 10);
        var book = new LevelTrackingOrderBook(security, Clock);
        var preOpen = book.UpdateStatus(OrderBookStatus.PreOpen);

        // assert - nothing resting yet, so nothing to quote
        Assert.IsEmpty(preOpen.OfType<IndicativePriceChanged>().ToList());

        // act - one-sided book, still no cross
        var oneSided = book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 10, 100);
        Assert.IsEmpty(oneSided.OfType<IndicativePriceChanged>().ToList());

        // act - a crossing sell arrives
        var crossed = book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Sell, 10, 100);
        var quote = crossed.OfType<IndicativePriceChanged>().Last();
        Assert.AreEqual(100, quote.Price);
        Assert.AreEqual(10, quote.Quantity);

        // act - more sell liquidity at the same price. The buy side still caps what could
        // clear, so the quote hasn't moved and nothing is published for it
        var deeperSell = book.CreateLimitOrder(CompanyId3, OrderId3, new OrderValidity.Day(), Side.Sell, 5, 100);
        Assert.IsEmpty(deeperSell.OfType<IndicativePriceChanged>().ToList());

        // act - the buy side catching up does move it
        var deeperBuy = book.CreateLimitOrder(CompanyId4, OrderId4, new OrderValidity.Day(), Side.Buy, 5, 100);
        var requoted = deeperBuy.OfType<IndicativePriceChanged>().Last();
        Assert.AreEqual(100, requoted.Price);
        Assert.AreEqual(15, requoted.Quantity);

        // act - cancelling every sell removes the cross again, withdrawing the quote
        var thinner = book.CancelOrder(CompanyId3, CancelId1, OrderId3);
        Assert.AreEqual(10, thinner.OfType<IndicativePriceChanged>().Last().Quantity);

        var uncrossed = book.CancelOrder(CompanyId2, CancelId1, OrderId2);
        var withdrawn = uncrossed.OfType<IndicativePriceChanged>().Last();
        Assert.IsNull(withdrawn.Price);
        Assert.AreEqual(0, withdrawn.Quantity);

        // and nothing more is published while there is still nothing to quote
        var stillNothing = book.CancelOrder(CompanyId1, CancelId1, OrderId1);
        Assert.IsEmpty(stillNothing.OfType<IndicativePriceChanged>().ToList());
    }

    [Test]
    public void RestingOrderOutsideBand_WithoutCrossingAnything_DoesNotPause()
    {
        // arrange - a resting limit order sitting far from the market doesn't represent any
        // actual price movement by itself; only an executed trade at an extreme price should
        // pause the book. This order doesn't cross anything (isolated at 200, nothing on the
        // opposing side), so it must not trigger a pause just for being entered.
        var security = new Instrument("GCZ6", 10, 10,
            PriceRestrictions: new PriceRestrictionConfig[] {new VolatilityBand(5)});
        var book = new LevelTrackingOrderBook(security, Clock);
        book.UpdateStatus(OrderBookStatus.Open, 100);

        // act
        var events = book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 10, 200);

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
        var security = new Instrument("GCZ6", 10, 10,
            PriceRestrictions: new PriceRestrictionConfig[] {new VolatilityBand(5)});
        var book = new LevelTrackingOrderBook(security, Clock);
        book.UpdateStatus(OrderBookStatus.Open);
        book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 5, 100);
        book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Sell, 5, 100);
        Clock.SetCurrentTime(Now2);
        book.CreateStopLimitOrder(CompanyId3, OrderId3, new OrderValidity.Day(), Side.Sell, 5, 80, 90);
        book.CreateLimitOrder(CompanyId4, OrderId4, new OrderValidity.Day(), Side.Sell, 10, 200);

        // act - a buy at 200 would actually cross and trade against the resting 200 sell, at a
        // price 100 away from the 100 reference - breaching the 50-wide volatility band. The
        // trade is prevented (not executed) and the book pauses instead; neither order fills
        Clock.SetCurrentTime(Now3);
        var events = book.CreateLimitOrder(CompanyId5, OrderId5, new OrderValidity.Day(), Side.Buy, 10, 200);

        // assert
        Assert.AreEqual(3, events.Count);
        Assert.IsInstanceOf<CreateOrderConfirmed>(events[0]);
        Assert.AreEqual(OrderBookStatus.Paused, ((StatusChanged) events[1]).Status);
        Assert.AreEqual(OrderBookStatus.Paused, book.Status);

        // the pause is an auction, and it quotes the crossed book it inherited rather than
        // printing it
        var paused = events[2] as IndicativePriceChanged;
        Assert.IsNotNull(paused);
        Assert.AreEqual(200, paused.Price);
        Assert.AreEqual(10, paused.Quantity);

        var sellLevels = book.GetLevels(Side.Sell, 10);
        Assert.AreEqual(1, sellLevels.Count);
        Assert.AreEqual(200, sellLevels[0].Price);
        Assert.AreEqual(10, sellLevels[0].Quantity); // untouched - the trade was prevented

        // orders can still be entered/cancelled while paused
        Clock.SetCurrentTime(Now4);
        book.CreateLimitOrder(CompanyId1, OrderId7, new OrderValidity.Day(), Side.Buy, 10, 90);
        book.CreateLimitOrder(CompanyId2, OrderId8, new OrderValidity.Day(), Side.Sell, 10, 90);

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
    public void EntryBandOnly_NoVolatilityBandConfigured_Unaffected()
    {
        // arrange - #23's hard-reject band still works exactly as before with no volatility
        // band alongside it
        var security = new Instrument("GCZ6", 10, 10,
            PriceRestrictions: new PriceRestrictionConfig[] {new OrderPriceBand(5)});
        var book = new LevelTrackingOrderBook(security, Clock);
        book.UpdateStatus(OrderBookStatus.Open, 100);

        // act
        var events = book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 5, 200);

        // assert - rejected outright, no pause
        var rejected = events[0] as CreateOrderRejected;
        Assert.IsNotNull(rejected);
        Assert.AreEqual(OrderRejectedReason.PriceOutsideBands, rejected.Reason);
        Assert.AreEqual(OrderBookStatus.Open, book.Status);
    }
}
