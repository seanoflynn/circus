using Circus.Events;
using Circus.MarketData;
using Circus.Sequencing;
using NUnit.Framework;

namespace Circus.Tests.Venues;

// A venue shaped like Eurex: the same instrument published on two interfaces carrying different
// products, and a service per product rather than per group.
//
// EOBI is order by order and full depth. EMDI is netted depth with the prints, ten deep. Both
// carry instrument state, because a subscriber to either needs to know the instrument is open -
// which is the detail that makes state a product rather than a channel's private business.
//
// Two interfaces on one book is the shape the whole restructure was aimed at, and it is a
// different shape from CME's rather than a rename of it: there, one subscription is the whole
// venue; here, a participant that wants depth and order identity subscribes twice.
public class EurexShapedVenueTests
{
    private const string GoldByOrder = "EOBI-GC";
    private const string GoldByPrice = "EMDI-GC";
    private const string SilverByOrder = "EOBI-SI";
    private const string SilverByPrice = "EMDI-SI";

    // EOBI's image is every resting order, which is the heaviest message a venue sends, so it
    // cycles slower than the depth image beside it.
    private const int ByOrderSnapshotEvery = 6;

    private static InstrumentGroup Venue(TimeSpan? snapshotInterval = null)
    {
        var group = new InstrumentGroup(VenueSession.Day, snapshotInterval);

        foreach (var (byOrder, byPrice) in new[]
                     {(GoldByOrder, GoldByPrice), (SilverByOrder, SilverByPrice)})
        {
            group.AddChannel(byOrder, FeedProducts.ByOrder | FeedProducts.Status,
                snapshotEvery: ByOrderSnapshotEvery);
            group.AddChannel(byPrice,
                FeedProducts.ByPrice | FeedProducts.Trades | FeedProducts.Status | FeedProducts.Indicative);
        }

        // A service per product, so each channel carries one instrument and leaves the other out.
        group.Add(VenueSession.Gold, VenueSession.Schedule, new[] {GoldByOrder, GoldByPrice});
        group.Add(VenueSession.Silver, VenueSession.Schedule, new[] {SilverByOrder, SilverByPrice});

        return group;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<ChannelMessage>> Run(
        TimeSpan? snapshotInterval = null) =>
        VenueSession.Run(Venue(snapshotInterval));

    private static string[] Kinds(IEnumerable<ChannelMessage> messages, ChannelStream stream) =>
        messages.Where(m => m.Stream == stream)
            .Select(m => m.Data.GetType().Name)
            .Distinct()
            .OrderBy(name => name)
            .ToArray();

    [Test]
    public void TheOrderByOrderInterface_CarriesOrdersAndStateAndNoLadder()
    {
        var published = Run();

        Assert.AreEqual(new[] {nameof(InstrumentStatusDataEvent), nameof(MarketByOrderDeltaEvent)},
            Kinds(published[GoldByOrder], ChannelStream.Incremental),
            "no aggregated depth and no prints - a subscriber that wants either subscribes to EMDI");
    }

    [Test]
    public void TheDepthInterface_CarriesTheLadderAndPrintsAndNoOrderIdentity()
    {
        var published = Run();

        Assert.AreEqual(new[]
            {
                nameof(IndicativePriceDataEvent), nameof(InstrumentStatusDataEvent),
                nameof(MarketByPriceDeltaEvent), nameof(TradeDataEvent)
            },
            Kinds(published[GoldByPrice], ChannelStream.Incremental));
    }

    // On EOBI an execution is an order event rather than a print: the same trade that EMDI
    // publishes as one TradeDataEvent arrives here as the two sides of it, paired by a shared id.
    // Without that id a consumer sees two fills at one price and cannot tell one trade between two
    // orders from two separate trades.
    [Test]
    public void AnExecution_ReachesTheOrderByOrderInterfaceAsPairedOrderEvents()
    {
        var published = Run();

        // The opening auction, which is the first thing either interface has a trade to report.
        var print = published[GoldByPrice].Select(m => m.Data).OfType<TradeDataEvent>().First();

        // By the print's own id, across interfaces, with nothing else to go on. Matching on time
        // and price would find the same two here and the wrong ones the moment a sweep prints
        // twice at one price in one action.
        var fills = published[GoldByOrder].Select(m => m.Data).OfType<MarketByOrderDeltaEvent>()
            .SelectMany(d => d.Changes)
            .Where(c => c.TradeId == print.TradeId)
            .ToList();

        Assert.AreEqual(2, fills.Count, "one per side of the trade");
        Assert.AreEqual(new[] {Side.Buy, Side.Sell}, fills.Select(f => f.Side).OrderBy(s => s).ToArray());
        Assert.IsTrue(fills.All(f => f.Action == OrderChangeAction.Filled));
        Assert.AreEqual(print.Price, fills[0].Price);
        Assert.AreEqual(print.Quantity, fills[0].Quantity);

        Assert.IsEmpty(published[GoldByOrder].Select(m => m.Data).OfType<TradeDataEvent>(),
            "EOBI carries the executions, not the trade summary");
    }

    // State on both, which is the reason it is a product rather than a channel's private business.
    [Test]
    public void InstrumentState_IsPublishedOnBothInterfaces()
    {
        var published = Run();

        foreach (var channel in new[] {GoldByOrder, GoldByPrice})
        {
            Assert.IsNotEmpty(published[channel].Select(m => m.Data).OfType<InstrumentStatusDataEvent>(),
                $"{channel} must say when the instrument opens");
        }
    }

    [Test]
    public void AServicePerProduct_CarriesThatProductAndNoOther()
    {
        var published = Run();

        Assert.AreEqual(new[] {VenueSession.Gold.Symbol},
            published[GoldByPrice].Select(m => m.Data.Symbol).Distinct().ToArray());
        Assert.AreEqual(new[] {VenueSession.Silver.Symbol},
            published[SilverByPrice].Select(m => m.Data.Symbol).Distinct().ToArray());
    }

    [Test]
    public void EachInterface_NumbersItsOwnMessages()
    {
        var published = Run();

        foreach (var (name, messages) in published)
        {
            var incremental = messages.Where(m => m.Stream == ChannelStream.Incremental).ToList();

            Assert.IsNotEmpty(incremental, $"{name} published nothing");
            Assert.AreEqual(Enumerable.Range(1, incremental.Count).Select(i => (long) i).ToArray(),
                incremental.Select(m => m.Sequence).ToArray(),
                $"{name} must number its own messages, with no gap where a sibling carried something");
        }
    }

    [Test]
    public void TheOrderByOrderImage_CyclesSlowerThanTheDepthImage()
    {
        var published = Run(TimeSpan.FromMinutes(10));

        var byOrder = SnapshotInstants(published[GoldByOrder]);
        var byPrice = SnapshotInstants(published[GoldByPrice]);

        Assert.IsNotEmpty(byOrder);
        Assert.AreEqual(byPrice.Length / ByOrderSnapshotEvery, byOrder.Length);
        Assert.IsTrue(byOrder.All(byPrice.Contains),
            "the slow interface restates on the venue's ticks, not on a schedule of its own");
    }

    private static DateTime[] SnapshotInstants(IEnumerable<ChannelMessage> messages) =>
        messages.Where(m => m.Stream == ChannelStream.Snapshot)
            .Select(m => m.Data.Time)
            .Distinct()
            .ToArray();
}
