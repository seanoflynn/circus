using Circus.Actions;
using Circus.MarketData;
using Circus.Time;
using NUnit.Framework;

namespace Circus.Tests.MarketData;

public class TradeDataProducerTests
{
    [Test]
    public void TradeDataProducer_Traded_FiresEvent()
    {
        // arrange
        var gold = new Instrument("GCZ6", 10, 10);
        var now = new DateTime(2000, 1, 1, 12, 0, 0);
        var clock = new ManualClock(now);
        var producer = new TradeDataProducer();

        var book = new TimestampingOrderBook(gold, clock);
        book.UpdateStatus(OrderBookStatus.Open);
        book.CreateLimitOrder("Company1", "Order1", new OrderValidity.Day(), Side.Buy, 3, 100);
        var bookEvents =
            book.CreateLimitOrder("Company2", "Order2", new OrderValidity.Day(), Side.Sell, 3, 100);

        // act
        var events = producer.Process(bookEvents);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(1, events.Count);
        Assert.AreEqual(now, events[0].Time);
        Assert.AreEqual(100, events[0].Price);
        Assert.AreEqual(3, events[0].Quantity);
    }

    // One print per trade, not per fill, when several trades land in one dispatch: an aggressor
    // sweeping two resting orders produces four fills across two trade ids, and a venue broadcasts
    // two prints. Deduplicating on the id changing is what makes that come out right.
    [Test]
    public void AnAggressorMatchingTwoRestingOrders_PrintsOncePerTrade()
    {
        var gold = new Instrument("GCZ6", 10, 10);
        var clock = new ManualClock(new DateTime(2000, 1, 1, 12, 0, 0));
        var producer = new TradeDataProducer();

        var book = new TimestampingOrderBook(gold, clock);
        book.UpdateStatus(OrderBookStatus.Open);
        book.CreateLimitOrder("Company1", "Order1", new OrderValidity.Day(), Side.Sell, 2, 100);
        book.CreateLimitOrder("Company2", "Order2", new OrderValidity.Day(), Side.Sell, 3, 110);

        var events = producer.Process(
            book.CreateLimitOrder("Company3", "Order3", new OrderValidity.Day(), Side.Buy, 5, 110));

        Assert.AreEqual(2, events.Count, "two trades, two prints - not four fills");
        Assert.AreEqual(new[] {100m, 110m}, events.Select(e => e.Price).ToArray());
        Assert.AreEqual(new[] {2, 3}, events.Select(e => e.Quantity).ToArray());
    }

    // The property that lets this hold nothing: what a batch produces does not depend on the
    // batches before it. A producer handed only the last dispatch says exactly what one that has
    // seen the whole session says, so there is no state a subscriber could be missing and none
    // that can go stale.
    [Test]
    public void WhatABatchProduces_DoesNotDependOnEarlierBatches()
    {
        var gold = new Instrument("GCZ6", 10, 10);
        var clock = new ManualClock(new DateTime(2000, 1, 1, 12, 0, 0));

        var book = new TimestampingOrderBook(gold, clock);
        var throughout = new TradeDataProducer();

        throughout.Process(book.UpdateStatus(OrderBookStatus.Open));
        throughout.Process(
            book.CreateLimitOrder("Company1", "Order1", new OrderValidity.Day(), Side.Buy, 3, 100));
        throughout.Process(
            book.CreateLimitOrder("Company2", "Order2", new OrderValidity.Day(), Side.Sell, 3, 100));
        throughout.Process(
            book.CreateLimitOrder("Company3", "Order3", new OrderValidity.Day(), Side.Buy, 4, 100));

        var lastDispatch =
            book.CreateLimitOrder("Company4", "Order4", new OrderValidity.Day(), Side.Sell, 4, 100);

        var seenEverything = throughout.Process(lastDispatch);
        var seenNothing = new TradeDataProducer().Process(lastDispatch);

        Assert.AreEqual(seenEverything, seenNothing);
        Assert.AreEqual(1, seenNothing.Count, "and it is a print, so the comparison is not of two empties");
    }
}
