using Circus.Events;
using Circus.Tests.Helpers;
using NUnit.Framework;

namespace Circus.Tests.Matching;

// An instrument names the algorithm its continuous trading allocates under, and the book builds
// it. ProRataMatchingAlgorithmTests covers what pro rata decides; these cover only that naming
// it is what a book actually runs - the seam that used to be missing, which left a complete
// algorithm reachable from nothing but its own tests.
//
// One scenario, run under each algorithm, because the selection is only demonstrated by the two
// disagreeing: a small early order and a large late one at the same price, taken by an aggressor
// too small to fill both. Time priority fills the early one outright; pro rata splits by size.
[TestFixture]
public class MatchingAlgorithmSelectionTests
{
    private static readonly DateTime Open = new(2000, 1, 1, 12, 0, 0);

    private static IOrderBook BookFor(MatchingAlgorithm algorithm)
    {
        var book = new OrderBook(new Instrument("GCZ6", 10, 10, MatchingAlgorithm: algorithm));
        book.OpenTrading(time: Open);

        // Early and small, then late and large, so the two algorithms rank them differently.
        book.CreateLimitOrder("Early", "E1", new OrderValidity.Day(), Side.Buy, 2, 100,
            time: Open.AddSeconds(1));
        book.CreateLimitOrder("Late", "L1", new OrderValidity.Day(), Side.Buy, 8, 100,
            time: Open.AddSeconds(2));

        return book;
    }

    private static Dictionary<string, int> FilledByCompany(IReadOnlyList<OrderBookEvent> events) =>
        events.Trades()
            .SelectMany(m => m.Fills)
            .Where(f => f.IsResting)
            .GroupBy(f => f.CompanyId)
            .ToDictionary(g => g.Key, g => g.Sum(f => f.Quantity));

    [Test]
    public void PriceTime_FillsTheEarlierOrderOutrightBeforeTheLaterOne()
    {
        var book = BookFor(MatchingAlgorithm.PriceTime);

        var events = book.CreateLimitOrder("Aggressor", "A1", new OrderValidity.Day(), Side.Sell, 5, 100,
            time: Open.AddSeconds(3));

        var filled = FilledByCompany(events);
        Assert.AreEqual(2, filled["Early"]);
        Assert.AreEqual(3, filled["Late"]);
    }

    [Test]
    public void ProRata_SplitsTheAggressorAcrossTheLevelBySize()
    {
        var book = BookFor(MatchingAlgorithm.ProRata);

        var events = book.CreateLimitOrder("Aggressor", "A1", new OrderValidity.Day(), Side.Sell, 5, 100,
            time: Open.AddSeconds(3));

        // Ten resting against an aggressor of five: a fifth of the level each way, so the early
        // order's two lots earn one and the late order's eight earn four. Arriving first bought
        // nothing, which is the whole difference from the test above.
        var filled = FilledByCompany(events);
        Assert.AreEqual(1, filled["Early"]);
        Assert.AreEqual(4, filled["Late"]);
    }

    [Test]
    public void AnInstrumentSayingNothingAllocatesUnderPriceTime()
    {
        Assert.AreEqual(MatchingAlgorithm.PriceTime, new Instrument("GCZ6", 10).MatchingAlgorithm);
    }
}
