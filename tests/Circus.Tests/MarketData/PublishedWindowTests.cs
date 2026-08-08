using Circus.Events;
using Circus.Tests.Helpers;
using NUnit.Framework;

namespace Circus.Tests.MarketData;

// The window a book publishes its levels across: OrderBook.PublishedDepth, for every book and
// every channel. These pin that it is honoured at both edges - that a level pushed past it is
// reported as leaving, and that a book holding less than a windowful is not reported as losing
// anything it never had.
//
// Fixed rather than configured, because a shallow delta stream is not a filtered deep one. That is
// the argument LevelsChanged makes and LevelWindowDiffTests asserts from both ends; what is left
// here is that the one window a book does publish behaves.
public class PublishedWindowTests
{
    private static readonly Instrument Gold = new("GCZ6", 10, 10);
    private static readonly DateTime Start = new(2000, 1, 1, 12, 0, 0);

    // Bids at descending prices, so a window shallower than the ladder is exercised by a real
    // ladder rather than by a book that happened to be shorter than the window.
    private static OrderBook BookWithBids(int count)
    {
        var book = new OrderBook(Gold);
        book.UpdateStatus(OrderBookStatus.Open, time: Start);

        for (var i = 0; i < count; i++)
        {
            book.CreateLimitOrder($"C{i}", $"O{i}", new OrderValidity.Day(), Side.Buy, 1, 200 - i * 10,
                time: Start.AddSeconds(i + 1));
        }

        return book;
    }

    // What a new best bid produces.
    private static LevelsChanged Report(OrderBook book) =>
        book.CreateLimitOrder("CX", "OX", new OrderValidity.Day(), Side.Buy, 1, 210,
                time: Start.AddMinutes(1))
            .OfType<LevelsChanged>().Single();

    private static BookSnapshot Snapshot(OrderBook book) =>
        book.Process(new Actions.PublishSnapshot {Symbol = Gold.Symbol, Time = Start.AddMinutes(1)})
            .OfType<BookSnapshot>().Single();

    [Test]
    public void ABookReportsTenDeep()
    {
        Assert.AreEqual(10, OrderBook.PublishedDepth);
        Assert.AreEqual(OrderBook.PublishedDepth, Report(BookWithBids(12)).Depth,
            "and says so on the report, so a consumer can read a departure at the last rank");
    }

    // A new best bid pushes whatever sat at the bottom of the window out of it. That Removed is the
    // reason the window has to be diffed rather than cut: the level is still on the book.
    [Test]
    public void ALevelPushedPastTheWindow_IsReportedAsLeavingIt()
    {
        var changes = Report(BookWithBids(12)).Changes;

        Assert.AreEqual(2, changes.Count);
        Assert.AreEqual(LevelChangeAction.Added, changes[0].Action);
        Assert.AreEqual(210, changes[0].Price);
        Assert.AreEqual(LevelChangeAction.Removed, changes[1].Action);
        Assert.AreEqual(110, changes[1].Price, "the tenth level, pushed past a ten-deep window");
        Assert.AreEqual(10, changes[1].LevelIndex, "carrying the rank it last held");
    }

    [Test]
    public void ABookHoldingLessThanAWindowful_LosesNothing()
    {
        var changes = Report(BookWithBids(3)).Changes;

        Assert.AreEqual(1, changes.Count,
            "nothing reached the edge of the window, so only the arrival is news");
        Assert.AreEqual(LevelChangeAction.Added, changes[0].Action);
        Assert.AreEqual(210, changes[0].Price);
    }

    [Test]
    public void ASnapshot_CarriesTheSameWindowTheReportsDo()
    {
        var bids = Snapshot(BookWithBids(12)).Bids;

        Assert.AreEqual(OrderBook.PublishedDepth, bids.Count,
            "a snapshot restates the window, so it is the same window the deltas move");
        Assert.AreEqual(new[] {200m, 190m, 180m, 170m, 160m, 150m, 140m, 130m, 120m, 110m},
            bids.Select(b => b.Price).ToArray());
    }

    // Order-by-order is a different product and deliberately not windowed: a depth feed publishes a
    // window because that is what fits on a wire, where an order-by-order feed carries the book.
    [Test]
    public void TheByOrderSnapshot_IsNotCappedByTheWindow()
    {
        var snapshot = Snapshot(BookWithBids(12));

        Assert.AreEqual(OrderBook.PublishedDepth, snapshot.Bids.Count);
        Assert.AreEqual(12, snapshot.Orders.Count, "every resting order, however deep it sits");
    }
}
