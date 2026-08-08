using Circus.Events;
using NUnit.Framework;

namespace Circus.Tests.MarketData;

// The diff at the heart of the by-price product: the published window before an action and after
// it, in, and the set of changes a subscriber applies, out.
//
// Driven directly rather than through a book. Every other test of this reaches it by arranging
// order flow, running it through OrderBook and reading the tail of an event list, which makes the
// interesting cases - a window boundary, a rank that moved without anything else changing - hard to
// set up and harder to read. Here the window is the input.
//
// Ticks are prices at a tick size of one, so the numbers below read as prices. TickSizeIsApplied
// covers the multiplication on its own.
public class LevelWindowDiffTests
{
    private const decimal OneToOne = 1m;

    private static List<(long Tick, int Quantity, int Count)> Window(
        params (long Tick, int Quantity, int Count)[] levels) => new(levels);

    // Best price outward, as a ladder hands it over: descending for bids.
    private static IReadOnlyList<LevelChange> Diff(
        List<(long Tick, int Quantity, int Count)> before,
        List<(long Tick, int Quantity, int Count)> after,
        int depth, decimal tickSize = OneToOne)
    {
        List<LevelChange> changes = null;
        DisplayedBookReport.CollectLevelChanges(ref changes, Side.Buy, before, after, depth, tickSize);
        return changes ?? (IReadOnlyList<LevelChange>) Array.Empty<LevelChange>();
    }

    [Test]
    public void NothingMoved_SaysNothing()
    {
        var window = Window((100, 5, 1), (90, 3, 2));

        Assert.IsEmpty(Diff(window, window, 10));
    }

    [Test]
    public void AnArrival_IsAddedAtTheRankItTook()
    {
        var changes = Diff(
            Window((100, 5, 1)),
            Window((110, 4, 1), (100, 5, 1)),
            10);

        Assert.AreEqual(1, changes.Count, "the level beneath it is unchanged and says nothing");
        Assert.AreEqual(LevelChangeAction.Added, changes[0].Action);
        Assert.AreEqual(110, changes[0].Price);
        Assert.AreEqual(4, changes[0].Quantity);
        Assert.AreEqual(1, changes[0].Count);
        Assert.AreEqual(1, changes[0].LevelIndex, "it arrived at the top");
    }

    [Test]
    public void AResizedLevel_IsModified()
    {
        var changes = Diff(
            Window((100, 5, 1)),
            Window((100, 9, 2)),
            10);

        Assert.AreEqual(1, changes.Count);
        Assert.AreEqual(LevelChangeAction.Modified, changes[0].Action);
        Assert.AreEqual(9, changes[0].Quantity);
        Assert.AreEqual(2, changes[0].Count);
    }

    // A level whose size is unchanged but which now holds a different number of orders has moved,
    // and a consumer tracking order counts needs to hear about it.
    [Test]
    public void ALevelWhoseCountMovedButSizeDidNot_IsModified()
    {
        var changes = Diff(
            Window((100, 6, 1)),
            Window((100, 6, 2)),
            10);

        Assert.AreEqual(1, changes.Count);
        Assert.AreEqual(LevelChangeAction.Modified, changes[0].Action);
    }

    [Test]
    public void AnEmptiedLevel_IsRemovedCarryingNothing()
    {
        var changes = Diff(
            Window((100, 5, 1), (90, 3, 1)),
            Window((100, 5, 1)),
            10);

        Assert.AreEqual(1, changes.Count);
        Assert.AreEqual(LevelChangeAction.Removed, changes[0].Action);
        Assert.AreEqual(90, changes[0].Price, "the price is what identifies which level left");
        Assert.AreEqual(0, changes[0].Quantity);
        Assert.AreEqual(0, changes[0].Count);
        Assert.AreEqual(2, changes[0].LevelIndex, "the rank it last held");
    }

    // The whole reason for keying on price rather than position. A better bid arriving pushes every
    // level below it down a rung, and none of them has anything to say about it.
    [Test]
    public void LevelsThatOnlyMovedRank_SayNothing()
    {
        var changes = Diff(
            Window((100, 5, 1), (90, 3, 1), (80, 2, 1)),
            Window((110, 7, 1), (100, 5, 1), (90, 3, 1), (80, 2, 1)),
            10);

        Assert.AreEqual(1, changes.Count, "only the arrival is news");
        Assert.AreEqual(110, changes[0].Price);
    }

    // The case LevelsChanged argues at length, and the reason a shallow report is not a filtered
    // deep one. Bids at 200 down to 150 with a new bid arriving at 195: ten deep only the arrival is
    // news, but five deep the arrival also pushes 160 out of the published window - a departure that
    // appears nowhere in the ten-deep report, at any rank. Truncating the deeper report by
    // LevelIndex would leave a five-deep subscriber holding a level nobody publishes any more.
    [Test]
    public void ANewBestLevel_PushesTheLastOneOutOfAShallowerWindowOnly()
    {
        var before = Window((200, 1, 1), (190, 1, 1), (180, 1, 1), (170, 1, 1), (160, 1, 1), (150, 1, 1));
        var after = Window((200, 1, 1), (195, 9, 1), (190, 1, 1), (180, 1, 1), (170, 1, 1), (160, 1, 1),
            (150, 1, 1));

        var deep = Diff(before, after, 10);

        Assert.AreEqual(1, deep.Count, "ten deep, the window holds everything and only 195 is news");
        Assert.AreEqual(LevelChangeAction.Added, deep[0].Action);
        Assert.AreEqual(195, deep[0].Price);

        var shallow = Diff(before, after, 5);

        Assert.AreEqual(2, shallow.Count, "five deep, 195 arriving also pushes 160 out of the window");
        Assert.AreEqual(LevelChangeAction.Added, shallow[0].Action);
        Assert.AreEqual(195, shallow[0].Price);
        Assert.AreEqual(LevelChangeAction.Removed, shallow[1].Action);
        Assert.AreEqual(160, shallow[1].Price,
            "still on the book, no longer published, and a subscriber must be told so");

        Assert.IsEmpty(deep.Where(c => c.Price == 160),
            "which is exactly the change truncating the ten-deep report would have lost");
    }

    // The other half of the same boundary: a level pushed out and then coming back is an arrival
    // again, because nothing published it in between.
    [Test]
    public void ALevelReturningToTheWindow_IsAddedAgain()
    {
        var pushedOut = Window((200, 1, 1), (195, 1, 1), (190, 1, 1), (180, 1, 1), (170, 1, 1));
        var backIn = Window((200, 1, 1), (190, 1, 1), (180, 1, 1), (170, 1, 1), (160, 1, 1));

        var changes = Diff(pushedOut, backIn, 5);

        Assert.AreEqual(2, changes.Count);
        Assert.AreEqual(LevelChangeAction.Added, changes[0].Action);
        Assert.AreEqual(160, changes[0].Price, "back inside the window, so an arrival");
        Assert.AreEqual(LevelChangeAction.Removed, changes[1].Action);
        Assert.AreEqual(195, changes[1].Price);
    }

    // So a consumer applying them in order builds the near side of the book before the far side,
    // and never sees a moment with two levels claiming one rank.
    [Test]
    public void ArrivalsAndChanges_ComeBeforeDepartures()
    {
        var changes = Diff(
            Window((100, 5, 1), (90, 3, 1)),
            Window((110, 4, 1), (100, 8, 2)),
            10);

        Assert.AreEqual(new[]
            {
                LevelChangeAction.Added, LevelChangeAction.Modified, LevelChangeAction.Removed
            },
            changes.Select(c => c.Action).ToArray());
        Assert.AreEqual(new[] {110m, 100m, 90m}, changes.Select(c => c.Price).ToArray(),
            "and best price outward within the arrivals");
    }

    [Test]
    public void AWindowShorterThanTheDepth_ReportsNoDeparturesItDidNotHave()
    {
        var changes = Diff(
            Window((100, 5, 1)),
            Window((100, 5, 1), (90, 2, 1)),
            10);

        Assert.AreEqual(1, changes.Count);
        Assert.AreEqual(LevelChangeAction.Added, changes[0].Action);
        Assert.AreEqual(90, changes[0].Price);
    }

    [Test]
    public void AnEmptiedSide_RemovesEveryLevelItHeld()
    {
        var changes = Diff(
            Window((100, 5, 1), (90, 3, 1)),
            Window(),
            10);

        Assert.AreEqual(2, changes.Count);
        Assert.IsTrue(changes.All(c => c.Action == LevelChangeAction.Removed));
        Assert.AreEqual(new[] {100m, 90m}, changes.Select(c => c.Price).ToArray());
        Assert.AreEqual(new[] {1, 2}, changes.Select(c => c.LevelIndex).ToArray(),
            "each carrying the rank it last held");
    }

    // Prices leave here as prices, and the ladder holds ticks - the one multiplication between them.
    [Test]
    public void TickSizeIsApplied()
    {
        var changes = Diff(
            Window(),
            Window((404, 5, 1)),
            10, tickSize: 0.25m);

        Assert.AreEqual(101m, changes[0].Price);
    }

    [Test]
    public void TheSideIsCarriedOntoEveryChange()
    {
        List<LevelChange> changes = null;
        DisplayedBookReport.CollectLevelChanges(ref changes, Side.Sell,
            Window((100, 5, 1)), Window((100, 9, 1)), 10, OneToOne);

        Assert.AreEqual(Side.Sell, changes.Single().Side);
    }

    // Both sides accumulate into one list, which is what lets a book report an action that moved
    // levels on each of them as a single LevelsChanged.
    [Test]
    public void BothSides_AccumulateIntoOneList()
    {
        List<LevelChange> changes = null;
        DisplayedBookReport.CollectLevelChanges(ref changes, Side.Buy,
            Window(), Window((100, 5, 1)), 10, OneToOne);
        DisplayedBookReport.CollectLevelChanges(ref changes, Side.Sell,
            Window(), Window((110, 4, 1)), 10, OneToOne);

        Assert.AreEqual(2, changes.Count);
        Assert.AreEqual(new[] {Side.Buy, Side.Sell}, changes.Select(c => c.Side).ToArray());
    }
}
