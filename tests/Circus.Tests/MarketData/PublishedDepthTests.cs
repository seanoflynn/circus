using Circus.Events;
using Circus.MarketData;
using Circus.Tests.Helpers;
using NUnit.Framework;

namespace Circus.Tests.MarketData;

// How deep a book reports its levels is a capability rather than a rule: it says how far it is
// willing to look, and a channel publishing less takes the top of what it is given. Ten by
// default, which is what CME's futures books carry, and the only value anything here uses today.
//
// It matters because a venue whose deepest product is twenty has to have a book looking twenty
// deep - a channel cannot truncate from a window that was never built. These pin that the number
// is honoured rather than approximated, at both ends and in the middle.
public class PublishedDepthTests
{
    private static readonly Instrument Gold = new("GCZ6", 10, 10);
    private static readonly DateTime Start = new(2000, 1, 1, 12, 0, 0);

    // Twelve resting bids at descending prices, so any depth up to twelve is exercised by a real
    // ladder rather than by a book that happened to be shallower than the window.
    private static OrderBook BookWithTwelveBids(int? publishedDepth = null)
    {
        var book = publishedDepth is { } depth ? new OrderBook(Gold, depth) : new OrderBook(Gold);
        book.UpdateStatus(OrderBookStatus.Open, time: Start);

        for (var i = 0; i < 12; i++)
        {
            book.CreateLimitOrder($"C{i}", $"O{i}", new OrderValidity.Day(), Side.Buy, 1, 200 - i * 10,
                time: Start.AddSeconds(i + 1));
        }

        return book;
    }

    private static IReadOnlyList<LevelChange> LastReport(OrderBook book) =>
        book.CreateLimitOrder("CX", "OX", new OrderValidity.Day(), Side.Buy, 1, 205,
                time: Start.AddMinutes(1))
            .OfType<LevelsChanged>().Single().Changes;

    [Test]
    public void ByDefault_ABookReportsTenDeep()
    {
        var book = BookWithTwelveBids();

        // A new best bid pushes the tenth level out of the window, so the report names it: an
        // Added at the top and a Removed for whatever fell off the bottom.
        var changes = LastReport(book);

        Assert.AreEqual(2, changes.Count);
        Assert.AreEqual(LevelChangeAction.Added, changes[0].Action);
        Assert.AreEqual(205, changes[0].Price);
        Assert.AreEqual(LevelChangeAction.Removed, changes[1].Action);
        Assert.AreEqual(110, changes[1].Price, "the tenth level, pushed past a ten-deep window");
    }

    [TestCase(1)]
    [TestCase(5)]
    [TestCase(10)]
    public void AShallowerBook_ReportsThatManyLevels(int depth)
    {
        var book = BookWithTwelveBids(depth);
        var changes = LastReport(book);

        Assert.AreEqual(LevelChangeAction.Added, changes[0].Action);
        Assert.AreEqual(205, changes[0].Price);
        Assert.AreEqual(LevelChangeAction.Removed, changes[1].Action);
        Assert.AreEqual(200 - (depth - 1) * 10, changes[1].Price,
            "whatever sat at the bottom of the window is what falls out of it");
    }

    // The case the parameter exists for. A twenty-deep book has all twelve levels inside its
    // window, so nothing falls out and a channel publishing five has twelve to truncate from.
    [Test]
    public void ADeeperBook_ReportsEverythingItHolds()
    {
        var book = BookWithTwelveBids(20);
        var changes = LastReport(book);

        Assert.AreEqual(1, changes.Count,
            "nothing left a window deeper than the book, so only the arrival is news");
        Assert.AreEqual(LevelChangeAction.Added, changes[0].Action);
        Assert.AreEqual(205, changes[0].Price);
    }

    [Test]
    public void ASnapshot_CarriesTheSameDepthTheReportsDo()
    {
        var book = BookWithTwelveBids(4);

        var snapshot = book.Process(new Actions.PublishSnapshot {Symbol = Gold.Symbol, Time = Start.AddMinutes(1)})
            .OfType<BookSnapshot>().Single();

        Assert.AreEqual(4, snapshot.Bids.Count,
            "a snapshot restates the window, so it is the same window the deltas move");
        Assert.AreEqual(new[] {200m, 190m, 180m, 170m}, snapshot.Bids.Select(b => b.Price).ToArray());
    }

    // Order-by-order is a different product and deliberately not windowed: a depth feed publishes
    // a window because that is what fits on a wire, where an order-by-order feed carries the book.
    [Test]
    public void TheByOrderSnapshot_IsNotCappedByPublishedDepth()
    {
        var book = BookWithTwelveBids(3);

        var snapshot = book.Process(new Actions.PublishSnapshot {Symbol = Gold.Symbol, Time = Start.AddMinutes(1)})
            .OfType<BookSnapshot>().Single();

        Assert.AreEqual(3, snapshot.Bids.Count);
        Assert.AreEqual(12, snapshot.Orders.Count, "every resting order, however deep it sits");
    }

    [Test]
    public void ABookReportingNoLevels_IsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OrderBook(Gold, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OrderBook(Gold, -1));
    }
}
