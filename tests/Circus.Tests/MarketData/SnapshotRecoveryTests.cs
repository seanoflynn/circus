using Circus.Actions;
using Circus.Agents;
using Circus.MarketData;
using Circus.Sequencing;
using Circus.Sessions;
using Circus.Tests.Helpers;
using NUnit.Framework;

namespace Circus.Tests.MarketData;

// What the snapshot feed is for. Every producer here holds either nothing or a composite it cannot
// rebuild, so the only answer to a subscriber that joined late or missed a message is a feed that
// periodically restates the truth. These assert it actually works, rather than that the messages
// have the right shape - which is the easy half.
//
// Driven off a recorded agent run rather than a hand-built sequence: recovery has to hold against
// churn it was not written for - repricing, icebergs replenishing, levels leaving the window and
// coming back - and a scripted trace only ever tests the cases someone thought of.
public class SnapshotRecoveryTests
{
    private static readonly Instrument Bench = new("BENCH", TickSize: 1);

    // Out of the way of a trace that starts at 09:00, so no session boundary lands inside the run.
    private static readonly MarketSchedule OutOfTheWay =
        new(new TimeSpan(22, 0, 0), new TimeSpan(22, 30, 0), new TimeSpan(23, 30, 0));

    private static IReadOnlyList<ChannelMessage> Run(TimeSpan? snapshotInterval, int actions = 400)
    {
        var trace = AgentTrace.Record(Bench, actions, seed: 4242);

        var group = new InstrumentGroup(trace[0].Time, snapshotInterval);
        group.Add(Bench, OutOfTheWay);
        group.Submit(new OpenTrading {Symbol = Bench.Symbol, Time = trace[0].Time});

        return Replay.Run(group, trace);
    }

    private static List<ChannelMessage> Of(IEnumerable<ChannelMessage> messages, ChannelStream stream) =>
        messages.Where(m => m.Stream == stream).ToList();

    [Test]
    public void NoInterval_PublishesNoSnapshots()
    {
        var messages = Run(snapshotInterval: null);

        Assert.IsNotEmpty(Of(messages, ChannelStream.Incremental), "the feed still runs");
        Assert.IsEmpty(Of(messages, ChannelStream.Snapshot),
            "a venue that publishes no snapshot feed publishes none");
    }

    [Test]
    public void EachStream_IsNumberedContiguouslyAndOnItsOwn()
    {
        var messages = Run(TimeSpan.FromMilliseconds(50));

        var incremental = Of(messages, ChannelStream.Incremental).Select(m => m.Sequence).ToList();
        var snapshots = Of(messages, ChannelStream.Snapshot).Select(m => m.Sequence).ToList();

        Assert.IsNotEmpty(snapshots, "the cycle should have come round during the run");
        Assert.AreEqual(Enumerable.Range(1, incremental.Count).Select(i => (long) i).ToList(), incremental,
            "a subscriber counting the incremental stream must see no gap");
        Assert.AreEqual(Enumerable.Range(1, snapshots.Count).Select(i => (long) i).ToList(), snapshots,
            "and the snapshot stream is numbered apart, not carved out of the same run");
    }

    [Test]
    public void ASnapshot_IsConsistentAsOfAnIncrementalAlreadyPublished()
    {
        var messages = Run(TimeSpan.FromMilliseconds(50));

        var publishedSoFar = 0L;
        var checkedAny = false;

        foreach (var message in messages)
        {
            if (message.Stream == ChannelStream.Incremental)
            {
                publishedSoFar = message.Sequence;
                continue;
            }

            checkedAny = true;
            Assert.AreEqual(publishedSoFar, message.AsOfSequence,
                "a snapshot must declare itself consistent as of everything published before it, " +
                "or a subscriber discarding its buffer up to that number drops messages it needs");
        }

        Assert.IsTrue(checkedAny);
    }

    // The whole mechanism, end to end. A subscriber that starts late buffers the incremental
    // stream, waits for a snapshot, applies it, discards the buffered messages up to and including
    // the sequence that snapshot declares, applies the rest, and carries on. If that is right it
    // ends up holding exactly what a subscriber who heard the whole session holds.
    [TestCase(0.25)]
    [TestCase(0.5)]
    [TestCase(0.75)]
    public void ASubscriberJoiningLate_RecoversToWhatEveryoneElseHolds(double joinFraction)
    {
        var messages = Run(TimeSpan.FromMilliseconds(50));
        var joinAt = (int) (messages.Count * joinFraction);

        // Heard everything: incrementals alone, which is what being in sync means.
        var everything = new LevelBook();
        foreach (var message in Of(messages, ChannelStream.Incremental))
            Apply(everything, message);

        // Joined at joinAt, hearing nothing before it.
        var late = new LevelBook();
        var buffered = new List<ChannelMessage>();
        var synced = false;

        foreach (var message in messages.Skip(joinAt))
        {
            if (message.Stream == ChannelStream.Snapshot)
            {
                if (synced || message.Data is not LevelsDataEvent image)
                    continue;

                late.Reset(image);

                // Everything the snapshot already reflects is dropped; the rest is replayed onto
                // it, in order.
                foreach (var pending in buffered.Where(m => m.Sequence > message.AsOfSequence))
                    Apply(late, pending);

                buffered.Clear();
                synced = true;
                continue;
            }

            if (synced)
                Apply(late, message);
            else
                buffered.Add(message);
        }

        Assert.IsTrue(synced, "a snapshot should have come round after the join point");
        Assert.AreEqual(everything.Bids, late.Bids,
            "a recovered subscriber holds a different book from one that heard everything (bids)");
        Assert.AreEqual(everything.Offers, late.Offers,
            "a recovered subscriber holds a different book from one that heard everything (offers)");
        Assert.IsNotEmpty(everything.Bids, "and the comparison is not of two empty books");
    }

    // The composite no incremental message carries whole: a joiner never heard the StatusChanged
    // that opened the book, and learns the status from the snapshot instead.
    [Test]
    public void ASnapshot_RestatesTheStatusAJoinerNeverHeard()
    {
        var messages = Run(TimeSpan.FromMilliseconds(50));

        var firstStatusIncremental = Of(messages, ChannelStream.Incremental)
            .Select(m => m.Data).OfType<InstrumentStatusDataEvent>().First();
        var statusFromSnapshot = Of(messages, ChannelStream.Snapshot)
            .Select(m => m.Data).OfType<InstrumentStatusDataEvent>().ToList();

        Assert.IsNotEmpty(statusFromSnapshot,
            "the snapshot stream carries the status composite, not only depth");
        Assert.AreEqual(OrderBookStatus.Open, firstStatusIncremental.Status);
        Assert.IsTrue(statusFromSnapshot.All(s => s.Status == OrderBookStatus.Open),
            "and it restates where the book actually is, so a joiner does not have to guess");
    }

    // Snapshots are dispatched actions like everything else, so a replay of the same trace
    // produces the same feed - the property that would have been lost had a snapshot been built by
    // asking the book instead of by going through the stream.
    [Test]
    public void ReplayingATrace_ReproducesTheSnapshotFeedToo()
    {
        var first = Run(TimeSpan.FromMilliseconds(50));
        var second = Run(TimeSpan.FromMilliseconds(50));

        Assert.IsNotEmpty(Of(first, ChannelStream.Snapshot));
        Assert.AreEqual(Render(first), Render(second));
    }

    private static void Apply(LevelBook book, ChannelMessage message)
    {
        if (message.Data is MarketByPriceDeltaEvent delta)
            book.Apply(delta);
    }

    private static List<string> Render(IEnumerable<ChannelMessage> messages) =>
        messages.Select(m => $"{m.Stream} {m.Sequence} {m.AsOfSequence} {m.Data}").ToList();
}
