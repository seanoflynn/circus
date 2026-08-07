using Circus.Events;
using Circus.MarketData;
using Circus.Tests.Helpers;
using NUnit.Framework;

namespace Circus.Tests.MarketData;

// A book emits two kinds of thing and they go to different places. What happened to one
// participant's order is addressed to that participant and carries the CompanyId saying whose it
// was; what happened to the book is broadcast and carries no such thing. Real venues keep the two
// apart at the protocol level - CME answers order entry on iLink and publishes market data on MDP -
// and here the line is drawn in the type system instead.
//
// These assert the line holds, and that the public half is genuinely its own view rather than the
// private half with fields removed: the two do not correspond one to one.
public class PublicPrivateSplitTests
{
    private static readonly Instrument Gold = new("GCZ6", 10, 10);

    private static readonly DateTime Now1 = new(2000, 1, 1, 12, 0, 0);
    private static readonly DateTime Now2 = new(2000, 1, 1, 12, 1, 0);
    private static readonly DateTime Now3 = new(2000, 1, 1, 12, 2, 0);
    private static readonly DateTime Now4 = new(2000, 1, 1, 12, 3, 0);

    private OrderBook _book = null!;

    [SetUp]
    public void SetUp() => _book = new OrderBook(Gold);

    private static IReadOnlyList<OrderChange> ChangesIn(IReadOnlyList<OrderBookEvent> events) =>
        events.OfType<OrdersChanged>().SelectMany(o => o.Changes).ToList();

    // The guarantee, stated over every published type rather than the one that happened to have a
    // test. A feed cannot leak client identity because it never sees any; this closes the other
    // half, that no broadcast type declares a place to put it.
    [Test]
    public void NoBroadcastEvent_DeclaresClientIdentity()
    {
        var offenders = typeof(MarketEvent).Assembly.GetTypes()
            .Where(t => typeof(MarketEvent).IsAssignableFrom(t))
            .SelectMany(t => t.GetProperties().Select(p => new {Type = t.Name, Property = p.Name}))
            .Where(x => x.Property is "CompanyId" or "ClientOrderId")
            .Select(x => $"{x.Type}.{x.Property}")
            .ToList();

        Assert.IsEmpty(offenders,
            "a broadcast event must never carry the originating client's identity");
    }

    [Test]
    public void ARestingOrder_IsConfirmedPrivatelyAndReportedPublicly()
    {
        _book.UpdateStatus(OrderBookStatus.Open, time: Now1);

        var events = _book.CreateLimitOrder("C1", "O1", new OrderValidity.Day(), Side.Buy, 3, 100,
            time: Now2);

        var confirmed = events.OfType<CreateOrderConfirmed>().Single();
        Assert.AreEqual("C1", confirmed.CompanyId, "the participant is told, and told whose it was");

        var change = ChangesIn(events).Single();
        Assert.AreEqual(OrderChangeAction.Added, change.Action);
        Assert.AreEqual(confirmed.Order.ExchangeOrderId, change.ExchangeOrderId,
            "the market is told, by the id it is allowed to know");
    }

    // One to zero. A stop that has not triggered is not on the displayed book, so there is a
    // confirmation for its owner and nothing for anyone else - which is why the public view cannot
    // be the private one with fields dropped.
    [Test]
    public void AHiddenStop_IsConfirmedButNotReported()
    {
        _book.UpdateStatus(OrderBookStatus.Open, time: Now1);
        _book.CreateLimitOrder("C1", "O1", new OrderValidity.Day(), Side.Buy, 3, 500, time: Now2);
        _book.CreateLimitOrder("C2", "O2", new OrderValidity.Day(), Side.Sell, 3, 500, time: Now2);

        var events = _book.CreateStopLimitOrder("C3", "O3", new OrderValidity.Day(), Side.Buy, 5, 530,
            510, time: Now3);

        Assert.IsNotEmpty(events.OfType<CreateOrderConfirmed>(), "its owner is told it was accepted");
        Assert.IsEmpty(ChangesIn(events), "and nobody else, because it is not on the displayed book");
    }

    // One to two. An update that loses time priority is a single confirmation to its owner and, to
    // the market, the old id leaving and a new one arriving at the back of the queue.
    [Test]
    public void ARepricedOrder_IsOneConfirmationAndTwoReportedChanges()
    {
        _book.UpdateStatus(OrderBookStatus.Open, time: Now1);
        _book.CreateLimitOrder("C1", "O1", new OrderValidity.Day(), Side.Buy, 6, 100, time: Now2);

        var events = _book.UpdateOrder("C1", "O1b", "O1", 6, 90, time: Now3);

        var updated = events.OfType<UpdateOrderConfirmed>().Single();
        var changes = ChangesIn(events);

        Assert.AreEqual(2, changes.Count);
        Assert.AreEqual(OrderChangeAction.Removed, changes[0].Action);
        Assert.AreEqual(updated.PreviousExchangeOrderId, changes[0].ExchangeOrderId);
        Assert.AreEqual(100, changes[0].Price, "the level it left");
        Assert.AreEqual(OrderChangeAction.Added, changes[1].Action);
        Assert.AreEqual(updated.Order.ExchangeOrderId, changes[1].ExchangeOrderId);
        Assert.AreEqual(90, changes[1].Price, "and the back of the queue it arrived at");
    }

    // Two to one. A trade is a fill for each participant and a single print for everyone.
    [Test]
    public void ATrade_IsTwoFillsAndOnePrint()
    {
        _book.UpdateStatus(OrderBookStatus.Open, time: Now1);
        _book.CreateLimitOrder("C1", "O1", new OrderValidity.Day(), Side.Buy, 3, 100, time: Now2);

        var events = _book.CreateLimitOrder("C2", "O2", new OrderValidity.Day(), Side.Sell, 3, 100,
            time: Now3);

        var fills = events.OfType<FillOrderConfirmed>().ToList();
        var print = events.OfType<TradePrinted>().Single();

        Assert.AreEqual(2, fills.Count, "one for each participant, each carrying their own CompanyId");
        Assert.AreEqual(new[] {"C1", "C2"}, fills.Select(f => f.CompanyId).ToArray());
        Assert.AreEqual(fills[0].TradeId, print.TradeId, "and one print, carrying the id that pairs them");
        Assert.AreEqual(100, print.Price);
        Assert.AreEqual(3, print.Quantity);
    }

    [Test]
    public void AnAggressorMatchingTwoOrders_PrintsOncePerTrade()
    {
        _book.UpdateStatus(OrderBookStatus.Open, time: Now1);
        _book.CreateLimitOrder("C1", "O1", new OrderValidity.Day(), Side.Sell, 2, 100, time: Now2);
        _book.CreateLimitOrder("C2", "O2", new OrderValidity.Day(), Side.Sell, 3, 110, time: Now2);

        var events = _book.CreateLimitOrder("C3", "O3", new OrderValidity.Day(), Side.Buy, 5, 110,
            time: Now3);

        var prints = events.OfType<TradePrinted>().ToList();
        Assert.AreEqual(2, prints.Count, "two trades, two prints - not four fills");
        Assert.AreEqual(new[] {100m, 110m}, prints.Select(p => p.Price).ToArray());
        Assert.AreEqual(2, prints.Select(p => p.TradeId).Distinct().Count());
    }

    [Test]
    public void AnActionTouchingNoOrder_ReportsNothingPublicly()
    {
        var events = _book.UpdateStatus(OrderBookStatus.Open, time: Now1);

        Assert.IsEmpty(events.OfType<OrdersChanged>(), "a status change moved no order");
        Assert.IsEmpty(events.OfType<TradePrinted>());
        Assert.IsNotEmpty(events.OfType<StatusChanged>(), "but the status change itself is published");
    }

    // The reason both halves stay in one stream: their order relative to each other is meaningful,
    // and a venue simulator is exactly where someone would ask about it.
    [Test]
    public void BothHalves_ArriveInOneStreamInOrder()
    {
        _book.UpdateStatus(OrderBookStatus.Open, time: Now1);
        _book.CreateLimitOrder("C1", "O1", new OrderValidity.Day(), Side.Buy, 3, 100, time: Now2);

        var events = _book.CreateLimitOrder("C2", "O2", new OrderValidity.Day(), Side.Sell, 3, 100,
            time: Now3);

        var lastFill = events.ToList().FindLastIndex(e => e is FillOrderConfirmed);
        var print = events.ToList().FindIndex(e => e is TradePrinted);

        Assert.Greater(print, lastFill,
            "the participants learn of their own fills before the market learns of the print");
    }
}
