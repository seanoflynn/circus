using Circus.Events;
using Circus.MarketData;
using Circus.Tests.Helpers;
using NUnit.Framework;

namespace Circus.Tests.MarketData;

// How deep a book reports its levels: the windows it diffs its ladders across, one report per
// window. Ten by default, which is what CME's futures books carry.
//
// A book can be asked for several windows at once, and then reports once per window. That is not
// an optimisation of "report deep and let each channel cut" - it is the only thing that works,
// because a shallow delta stream is not a filtered deep one. AShallowReport_CarriesDepartures-
// TheDeepReportDoesNot below is the case that says why.
//
// These pin that each number is honoured rather than approximated, at both ends and in the middle.
public class PublishedDepthTests
{
    private static readonly Instrument Gold = new("GCZ6", 10, 10);
    private static readonly DateTime Start = new(2000, 1, 1, 12, 0, 0);

    // Twelve resting bids at descending prices, so any depth up to twelve is exercised by a real
    // ladder rather than by a book that happened to be shallower than the window.
    private static OrderBook BookWithTwelveBids(int? publishedDepth = null) =>
        Fill(publishedDepth is { } depth ? new OrderBook(Gold, depth) : new OrderBook(Gold));

    private static OrderBook BookWithTwelveBids(IReadOnlyList<int> publishedDepths) =>
        Fill(new OrderBook(Gold, publishedDepths));

    private static OrderBook Fill(OrderBook book)
    {
        book.UpdateStatus(OrderBookStatus.Open, time: Start);

        for (var i = 0; i < 12; i++)
        {
            book.CreateLimitOrder($"C{i}", $"O{i}", new OrderValidity.Day(), Side.Buy, 1, 200 - i * 10,
                time: Start.AddSeconds(i + 1));
        }

        return book;
    }

    private static IReadOnlyList<LevelChange> LastReport(OrderBook book) =>
        Reports(book).Single().Changes;

    // Every report a new best bid produces, in the order the book emitted them.
    private static IReadOnlyList<LevelsChanged> Reports(OrderBook book) =>
        book.CreateLimitOrder("CX", "OX", new OrderValidity.Day(), Side.Buy, 1, 210,
                time: Start.AddMinutes(1))
            .OfType<LevelsChanged>().ToList();

    [Test]
    public void ByDefault_ABookReportsTenDeep()
    {
        var book = BookWithTwelveBids();

        // A new best bid pushes the tenth level out of the window, so the report names it: an
        // Added at the top and a Removed for whatever fell off the bottom.
        var changes = LastReport(book);

        Assert.AreEqual(2, changes.Count);
        Assert.AreEqual(LevelChangeAction.Added, changes[0].Action);
        Assert.AreEqual(210, changes[0].Price);
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
        Assert.AreEqual(210, changes[0].Price);
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
        Assert.AreEqual(210, changes[0].Price);
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
        Assert.Throws<ArgumentOutOfRangeException>(() => new OrderBook(Gold, new[] {10, 0}));
        Assert.Throws<ArgumentException>(() => new OrderBook(Gold, Array.Empty<int>()));
    }

    // A book can be asked for more than one window, which is what a venue publishing the same
    // instrument's depth in two shapes needs - CME's top-of-book channel beside its ten-deep one,
    // Databento's mbp-1 beside mbp-10.
    [Test]
    public void ABookAskedForSeveralDepths_ReportsOnceForEach()
    {
        var book = BookWithTwelveBids(new[] {10, 1});

        var reports = Reports(book);

        Assert.AreEqual(new[] {1, 10}, reports.Select(r => r.Depth).ToArray(),
            "shallowest first, so the order is the book's and not the order it was configured in");
    }

    [Test]
    public void TheSameDepthAskedForTwice_IsReportedOnce()
    {
        var book = BookWithTwelveBids(new[] {10, 10, 10});

        Assert.AreEqual(new[] {10}, Reports(book).Select(r => r.Depth).ToArray());
    }

    // The case that decides the whole design. A new best bid at ten deep is one Added and nothing
    // else: the levels beneath it only moved rank, and price-keyed reporting deliberately says
    // nothing about that. At one deep the same action pushed 200 out of the window, so the report
    // has to say so - and that Removed appears nowhere in the ten-deep report, at any rank.
    //
    // So a shallow feed cannot be built by filtering a deep report by LevelIndex. It has to be
    // diffed at its own depth, which is why the book reports once per depth rather than once.
    [Test]
    public void AShallowReport_CarriesDeparturesTheDeepReportDoesNot()
    {
        var book = BookWithTwelveBids(new[] {1, 20});

        var reports = Reports(book).ToDictionary(r => r.Depth);

        Assert.AreEqual(1, reports[20].Changes.Count, "deeper than the book, so only the arrival is news");
        Assert.AreEqual(LevelChangeAction.Added, reports[20].Changes[0].Action);
        Assert.AreEqual(210, reports[20].Changes[0].Price);

        Assert.AreEqual(2, reports[1].Changes.Count);
        Assert.AreEqual(LevelChangeAction.Added, reports[1].Changes[0].Action);
        Assert.AreEqual(210, reports[1].Changes[0].Price);
        Assert.AreEqual(LevelChangeAction.Removed, reports[1].Changes[1].Action);
        Assert.AreEqual(200, reports[1].Changes[1].Price,
            "the old best bid left a one-deep window, and no deeper report mentions it");
    }

    // The same thing away from the top of the book, so it is not an artefact of depth one.
    [Test]
    public void ALevelPushedPastAShallowWindow_IsReportedAsLeavingIt()
    {
        var book = BookWithTwelveBids(new[] {5, 20});

        var reports = Reports(book).ToDictionary(r => r.Depth);

        Assert.AreEqual(1, reports[20].Changes.Count,
            "nothing left a window deeper than the book");
        Assert.AreEqual(new[] {(LevelChangeAction.Added, 210m), (LevelChangeAction.Removed, 160m)},
            reports[5].Changes.Select(c => (c.Action, c.Price)).ToArray(),
            "five deep, the fifth-best bid is now sixth and out of the window");
    }

    // Depths added after the book was built, for a channel declared after its instruments. Which
    // is what InstrumentGroup does, so declaring channels and adding instruments still commute.
    [Test]
    public void ADepthAddedLater_IsReportedFromThenOn()
    {
        var book = BookWithTwelveBids();
        book.AlsoReport(1);
        book.AlsoReport(1);

        Assert.AreEqual(new[] {1, 10}, book.PublishedDepths.ToArray(),
            "added once, however many times it is asked for");
        Assert.AreEqual(new[] {1, 10}, Reports(book).Select(r => r.Depth).ToArray());
    }

    // The window a snapshot carries is the deepest, and a feed publishing less takes the top of
    // it - which unlike a delta is a plain cut, since an image says where the book is.
    [Test]
    public void ASnapshotOfAMultiDepthBook_CarriesTheDeepestWindow()
    {
        var book = BookWithTwelveBids(new[] {1, 4});

        var snapshot = book.Process(new Actions.PublishSnapshot {Symbol = Gold.Symbol, Time = Start.AddMinutes(1)})
            .OfType<BookSnapshot>().Single();

        Assert.AreEqual(new[] {200m, 190m, 180m, 170m}, snapshot.Bids.Select(b => b.Price).ToArray());
    }
}
