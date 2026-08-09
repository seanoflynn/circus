using Circus.MarketData;
using Circus.Sequencing;
using NUnit.Framework;

namespace Circus.Tests.Venues;

// A venue shaped like CME's MDP 3.0: one channel per product group, carrying every product about
// every instrument in the group.
//
// A subscriber takes one channel and has the whole complex - depth, order by order, prints and
// instrument status arriving under one numbering. That is what makes a product group the unit of
// a channel there: a spread and its legs need a common order the moment implied pricing exists,
// and two channels do not give them one.
//
// Everything here is the group's configuration and nothing else. The claim being made is that the
// shape is expressible, so any test that reached past AddChannel to get its answer would not be
// making it.
public class CmeShapedVenueTests
{
    // Named as CME names them, by product group. One channel, every product on it.
    private const string Channel = "310";

    private static InstrumentGroup Venue(TimeSpan? snapshotInterval = null)
    {
        var group = new InstrumentGroup(VenueSession.Day, snapshotInterval);

        group.AddChannel(Channel, FeedProducts.All);
        group.Add(VenueSession.Gold, VenueSession.Schedule);
        group.Add(VenueSession.Silver, VenueSession.Schedule);

        return group;
    }

    private static IReadOnlyList<ChannelMessage> Run(TimeSpan? snapshotInterval = null) =>
        VenueSession.Run(Venue(snapshotInterval))[Channel];

    private static string[] Kinds(IEnumerable<ChannelMessage> messages, ChannelStream stream) =>
        messages.Where(m => m.Stream == stream)
            .Select(m => m.Data.GetType().Name)
            .Distinct()
            .OrderBy(name => name)
            .ToArray();

    [Test]
    public void OneSubscription_CarriesEveryProduct()
    {
        var messages = Run();

        Assert.AreEqual(new[]
            {
                nameof(IndicativePriceDataEvent), nameof(InstrumentStatusDataEvent),
                nameof(MarketByOrderDeltaEvent), nameof(MarketByPriceDeltaEvent), nameof(TradeDataEvent)
            },
            Kinds(messages, ChannelStream.Incremental),
            "depth, order by order, prints and state all arrive on the one channel");
    }

    [Test]
    public void OneSubscription_CarriesEveryInstrumentInTheGroup()
    {
        var messages = Run();

        Assert.AreEqual(new[] {VenueSession.Gold.Symbol, VenueSession.Silver.Symbol},
            messages.Select(m => m.Data.Symbol).Distinct().OrderBy(s => s).ToArray());
    }

    // The property a channel's numbering exists for, across instruments rather than within one:
    // a gap is loss and nothing else, so a subscriber counting the run can tell it has missed a
    // message without knowing what the venue chose not to send it.
    [Test]
    public void TheChannel_NumbersItsMessagesContiguouslyAcrossInstruments()
    {
        var messages = Run().Where(m => m.Stream == ChannelStream.Incremental).ToList();

        Assert.IsNotEmpty(messages);
        Assert.AreEqual(Enumerable.Range(1, messages.Count).Select(i => (long) i).ToArray(),
            messages.Select(m => m.Sequence).ToArray());
    }

    [Test]
    public void TheDepthProduct_RunsTenDeep()
    {
        var deltas = Run().Select(m => m.Data).OfType<MarketByPriceDeltaEvent>().ToList();

        Assert.IsNotEmpty(deltas);
        Assert.IsTrue(deltas.All(d => d.Depth == VenueSession.Depth));
    }

    // One matching-engine event is one message per product, however many levels or orders it
    // moved. The aggressor in the session sweeps three offer levels; a shape that reported them
    // one at a time would leave every subscriber to recover the grouping before it could act.
    [Test]
    public void AnAggressorSweepingThreeLevels_IsOneMessageOnEachProduct()
    {
        var messages = Run();

        var sweep = messages.Select(m => m.Data).OfType<MarketByPriceDeltaEvent>()
            .Single(d => d.Changes.Count(c => c.Side == Side.Sell) == 3);

        Assert.AreEqual(3, sweep.Changes.Count(c => c.Side == Side.Sell));

        var atSameInstant = messages.Select(m => m.Data)
            .OfType<MarketByOrderDeltaEvent>()
            .Count(d => d.Time == sweep.Time);

        Assert.AreEqual(1, atSameInstant,
            "the by-order product reports the same event as one message too");
    }

    // The recovery half, on the same channel and numbered apart from the updates - CME publishes
    // it on a separate connection, which is the same separation as a separate stream here.
    [Test]
    public void TheSnapshotStream_RestatesEveryProductThatHasAnImage()
    {
        var messages = Run(TimeSpan.FromMinutes(10));

        Assert.AreEqual(new[]
            {
                nameof(IndicativePriceDataEvent), nameof(InstrumentStatusDataEvent),
                nameof(LevelsDataEvent), nameof(OrdersDataEvent)
            },
            Kinds(messages, ChannelStream.Snapshot),
            "a stream of prints has no image, so trades are not restated");
    }

    [Test]
    public void TheSnapshotStream_IsNumberedApartFromTheUpdates()
    {
        var messages = Run(TimeSpan.FromMinutes(10));
        var snapshots = messages.Where(m => m.Stream == ChannelStream.Snapshot).ToList();

        Assert.IsNotEmpty(snapshots);
        Assert.AreEqual(Enumerable.Range(1, snapshots.Count).Select(i => (long) i).ToArray(),
            snapshots.Select(m => m.Sequence).ToArray());
    }
}
