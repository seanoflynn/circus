using Circus.Actions;
using Circus.Events;
using Circus.MarketData;
using Circus.Time;
using Circus.Tests.Helpers;
using NUnit.Framework;

namespace Circus.Tests.MarketData;

public class TradeFeedTests
{
    [Test]
    public void Traded_PublishesAPrint()
    {
        // arrange
        var gold = new Instrument("GCZ6", 10, 10);
        var now = new DateTime(2000, 1, 1, 12, 0, 0);
        var clock = new ManualClock(now);
        var feed = ProductFeed.Carrying(FeedProducts.Trades);

        var book = new TimestampingOrderBook(gold, clock);
        book.UpdateStatus(OrderBookStatus.Open);
        book.CreateLimitOrder("Company1", "Order1", new OrderValidity.Day(), Side.Buy, 3, 100);
        var bookEvents =
            book.CreateLimitOrder("Company2", "Order2", new OrderValidity.Day(), Side.Sell, 3, 100);

        // act
        var events = feed.Publish<TradeDataEvent>(bookEvents);

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
        var feed = ProductFeed.Carrying(FeedProducts.Trades);

        var book = new TimestampingOrderBook(gold, clock);
        book.UpdateStatus(OrderBookStatus.Open);
        book.CreateLimitOrder("Company1", "Order1", new OrderValidity.Day(), Side.Sell, 2, 100);
        book.CreateLimitOrder("Company2", "Order2", new OrderValidity.Day(), Side.Sell, 3, 110);

        var events = feed.Publish<TradeDataEvent>(
            book.CreateLimitOrder("Company3", "Order3", new OrderValidity.Day(), Side.Buy, 5, 110));

        Assert.AreEqual(2, events.Count, "two trades, two prints - not four fills");
        Assert.AreEqual(new[] {100m, 110m}, events.Select(e => e.Price).ToArray());
        Assert.AreEqual(new[] {2, 3}, events.Select(e => e.Quantity).ToArray());
        Assert.AreEqual(2, events.Select(e => e.TradeId).Distinct().Count(),
            "an id apiece - two prints sharing one would say they were the same trade");
    }

    // The id is the book's, not the feed's. It is what the two fills of the trade carry, so a
    // subscriber holding the by-order feed as well can join a print to the order events that made
    // it - and a participant can find its own fill inside a print it was part of.
    [Test]
    public void APrint_CarriesTheIdTheFillsOfItsTradeCarry()
    {
        var gold = new Instrument("GCZ6", 10, 10);
        var clock = new ManualClock(new DateTime(2000, 1, 1, 12, 0, 0));

        var book = new TimestampingOrderBook(gold, clock);
        book.UpdateStatus(OrderBookStatus.Open);
        book.CreateLimitOrder("Company1", "Order1", new OrderValidity.Day(), Side.Buy, 3, 100);

        var bookEvents =
            book.CreateLimitOrder("Company2", "Order2", new OrderValidity.Day(), Side.Sell, 3, 100);

        var print = ProductFeed.Carrying(FeedProducts.Trades).Publish<TradeDataEvent>(bookEvents).Single();
        var fills = bookEvents.OfType<FillOrderConfirmed>().ToList();

        Assert.AreEqual(2, fills.Count, "one per side");
        Assert.IsNotEmpty(print.TradeId);
        Assert.IsTrue(fills.All(f => f.TradeId == print.TradeId));
    }

    // And the same join against the public by-order product, which is where a subscriber actually
    // meets it - a fill event is private and never reaches a feed.
    [Test]
    public void APrint_JoinsToTheByOrderMessageForTheSameTrade()
    {
        var gold = new Instrument("GCZ6", 10, 10);
        var clock = new ManualClock(new DateTime(2000, 1, 1, 12, 0, 0));

        var book = new TimestampingOrderBook(gold, clock);
        book.UpdateStatus(OrderBookStatus.Open);
        book.CreateLimitOrder("Company1", "Order1", new OrderValidity.Day(), Side.Buy, 3, 100);

        var bookEvents =
            book.CreateLimitOrder("Company2", "Order2", new OrderValidity.Day(), Side.Sell, 3, 100);

        var print = ProductFeed.Carrying(FeedProducts.Trades).Publish<TradeDataEvent>(bookEvents).Single();

        var filled = ProductFeed.Carrying(FeedProducts.ByOrder).Publish<MarketByOrderDeltaEvent>(bookEvents)
            .SelectMany(message => message.Changes)
            .Where(change => change.TradeId == print.TradeId)
            .ToList();

        Assert.AreEqual(2, filled.Count, "the two sides of the print, found by its id alone");
        Assert.IsTrue(filled.All(change => change.Action == OrderChangeAction.Filled));
        Assert.AreEqual(new[] {Side.Buy, Side.Sell}, filled.Select(c => c.Side).OrderBy(s => s).ToArray());
    }

    // The property that lets this hold nothing: what a batch produces does not depend on the
    // batches before it. A feed handed only the last dispatch says exactly what one that has
    // seen the whole session says, so there is no state a subscriber could be missing and none
    // that can go stale.
    [Test]
    public void WhatABatchProduces_DoesNotDependOnEarlierBatches()
    {
        var gold = new Instrument("GCZ6", 10, 10);
        var clock = new ManualClock(new DateTime(2000, 1, 1, 12, 0, 0));

        var book = new TimestampingOrderBook(gold, clock);
        var throughout = ProductFeed.Carrying(FeedProducts.Trades);

        throughout.Publish<TradeDataEvent>(book.UpdateStatus(OrderBookStatus.Open));
        throughout.Publish<TradeDataEvent>(
            book.CreateLimitOrder("Company1", "Order1", new OrderValidity.Day(), Side.Buy, 3, 100));
        throughout.Publish<TradeDataEvent>(
            book.CreateLimitOrder("Company2", "Order2", new OrderValidity.Day(), Side.Sell, 3, 100));
        throughout.Publish<TradeDataEvent>(
            book.CreateLimitOrder("Company3", "Order3", new OrderValidity.Day(), Side.Buy, 4, 100));

        var lastDispatch =
            book.CreateLimitOrder("Company4", "Order4", new OrderValidity.Day(), Side.Sell, 4, 100);

        var seenEverything = throughout.Publish<TradeDataEvent>(lastDispatch);
        var seenNothing = ProductFeed.Carrying(FeedProducts.Trades).Publish<TradeDataEvent>(lastDispatch);

        Assert.AreEqual(seenEverything, seenNothing);
        Assert.AreEqual(1, seenNothing.Count, "and it is a print, so the comparison is not of two empties");
    }
}
