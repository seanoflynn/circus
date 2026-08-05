using Circus.Actions;
using Circus.MarketData;
using Circus.Sequencing;
using Circus.Sessions;
using NUnit.Framework;

namespace Circus.Tests.Sequencing;

// How often a channel restates itself, declared per channel and counted in the group's ticks.
//
// The venue has one snapshot schedule and ticks at the finest cadence any of its channels wants;
// a channel wanting a slower one skips ticks. That is one interval and a counter per channel
// rather than several schedules to keep in step, and it is the shape real venues have for a
// concrete reason: a full order-by-order image is the heaviest message a venue sends, so it
// cycles slower than the depth image beside it.
public class SnapshotCadenceTests
{
    private static readonly Instrument Gold = new("GCZ6", 10, 10);
    private static readonly DateTime Day = new(2000, 1, 3, 8, 0, 0);

    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(10);

    // Out of the way of a run that starts at 08:00, so no session boundary lands inside it - a
    // transition falling on a snapshot instant is its own question and not this one.
    private static readonly MarketSchedule OutOfTheWay =
        new(new TimeSpan(22, 0, 0), new TimeSpan(22, 30, 0), new TimeSpan(23, 30, 0));

    private static CreateLimitOrder Bid(int index, decimal price) =>
        new()
        {
            Symbol = Gold.Symbol, Time = Day.AddMinutes(45).AddSeconds(index), CompanyId = "C1",
            ClientOrderId = $"O{index}", OrderValidity = new OrderValidity.Day(), Side = Side.Buy,
            Quantity = 1, Price = price
        };

    // Runs the group forward over six snapshot ticks, and returns what each channel published.
    private static Dictionary<string, List<ChannelMessage>> RunSixTicks(InstrumentGroup group,
        int orders = 1)
    {
        var published = group.ChannelNames.ToDictionary(n => n, _ => new List<ChannelMessage>());

        group.Submit(new OpenTrading {Symbol = Gold.Symbol, Time = Day.AddMinutes(1)});

        for (var i = 0; i < orders; i++)
            group.Submit(Bid(i, 200 - i * 10));

        foreach (var dispatched in group.Sequencer.AdvanceTo(Day + Tick * 6))
        {
            foreach (var name in group.ChannelNames)
                published[name].AddRange(group.ChannelNamed(name).Publish(dispatched.Events));
        }

        return published;
    }

    // Distinct, because a channel carrying several products publishes several messages per tick
    // and what is being counted here is the ticks.
    private static DateTime[] SnapshotTimes(IEnumerable<ChannelMessage> messages) =>
        messages.Where(m => m.Stream == ChannelStream.Snapshot)
            .Select(m => m.Data.Time)
            .Distinct()
            .ToArray();

    [Test]
    public void ByDefault_AChannelRestatesItselfOnEveryTick()
    {
        var group = new InstrumentGroup(Day, Tick);
        group.Add(Gold, OutOfTheWay);

        var times = SnapshotTimes(RunSixTicks(group)[MarketDataChannel.DefaultName]);

        Assert.AreEqual(6, times.Length);
        Assert.AreEqual(Day + Tick, times[0]);
        Assert.AreEqual(Day + Tick * 6, times[^1]);
    }

    // The point of the whole thing: two channels on one schedule, restating at different rates.
    [Test]
    public void TwoChannels_CanRestateAtDifferentRates()
    {
        var group = new InstrumentGroup(Day, Tick);
        group.AddChannel("fast", FeedProducts.ByPrice);
        group.AddChannel("slow", FeedProducts.ByOrder, snapshotEvery: 3);
        group.Add(Gold, OutOfTheWay);

        var published = RunSixTicks(group);

        Assert.AreEqual(6, SnapshotTimes(published["fast"]).Length);
        Assert.AreEqual(new[] {Day + Tick * 3, Day + Tick * 6}, SnapshotTimes(published["slow"]),
            "every third tick, and on the third rather than the first - a joiner waits at most a " +
            "full cycle, which is what a cycle means");
    }

    // A skipped tick is not a gap. The snapshot stream is numbered per channel, so a channel that
    // publishes on one tick in three counts one, two, three - not one, four, seven.
    [Test]
    public void ASlowChannel_NumbersOnlyWhatItPublishes()
    {
        var group = new InstrumentGroup(Day, Tick);
        group.AddChannel("slow", FeedProducts.ByPrice, snapshotEvery: 3);
        group.Add(Gold, OutOfTheWay);

        var snapshots = RunSixTicks(group)["slow"]
            .Where(m => m.Stream == ChannelStream.Snapshot)
            .ToList();

        Assert.IsNotEmpty(snapshots);
        Assert.AreEqual(Enumerable.Range(1, snapshots.Count).Select(i => (long) i).ToArray(),
            snapshots.Select(m => m.Sequence).ToArray());
    }

    // Skipping a tick withholds the image, not the updates. A subscriber in sync reads only the
    // incremental stream, so a slow snapshot cycle costs a joiner time and costs nobody else
    // anything - which is why a venue can afford to cycle its heaviest product slowly.
    [Test]
    public void ASlowChannel_StillPublishesEveryIncremental()
    {
        var group = new InstrumentGroup(Day, Tick);
        group.AddChannel("fast", FeedProducts.ByPrice);
        group.AddChannel("slow", FeedProducts.ByPrice, snapshotEvery: 3);
        group.Add(Gold, OutOfTheWay);

        var published = RunSixTicks(group, orders: 4);

        var fast = published["fast"].Count(m => m.Stream == ChannelStream.Incremental);
        Assert.AreNotEqual(0, fast);
        Assert.AreEqual(fast, published["slow"].Count(m => m.Stream == ChannelStream.Incremental));
    }

    // The snapshot a slow channel does publish is stamped with where its own incremental stream
    // stands, which is what a joiner discards its buffer up to. Skipping ticks must not leave that
    // pointing anywhere but the end of what the channel has actually published.
    [Test]
    public void TheSnapshotASlowChannelPublishes_IsAsOfWhereItsIncrementalStreamStands()
    {
        var group = new InstrumentGroup(Day, Tick);
        group.AddChannel("slow", FeedProducts.ByPrice, snapshotEvery: 3);
        group.Add(Gold, OutOfTheWay);

        var messages = RunSixTicks(group, orders: 4);
        var snapshot = messages["slow"].Last(m => m.Stream == ChannelStream.Snapshot);
        var incrementals = messages["slow"].Where(m => m.Stream == ChannelStream.Incremental).ToList();

        Assert.IsNotEmpty(incrementals);
        Assert.AreEqual(incrementals[^1].Sequence, snapshot.AsOfSequence,
            "as of everything the channel had published by then, skipped ticks included");
    }

    [Test]
    public void AChannelThatSkipsEveryTick_IsRefused()
    {
        var group = new InstrumentGroup(Day, Tick);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => group.AddChannel("never", FeedProducts.ByPrice, snapshotEvery: 0));
    }

    // A cadence is counted in snapshot ticks rather than dispatches, so how often a channel
    // restates itself does not depend on how busy the instrument is.
    [Test]
    public void OrderFlowBetweenTicks_DoesNotMoveTheCount()
    {
        var group = new InstrumentGroup(Day, Tick);
        group.AddChannel("slow", FeedProducts.ByPrice, snapshotEvery: 3);
        group.Add(Gold, OutOfTheWay);

        var published = RunSixTicks(group, orders: 15);

        Assert.AreEqual(new[] {Day + Tick * 3, Day + Tick * 6}, SnapshotTimes(published["slow"]));
    }
}
