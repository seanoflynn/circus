using Circus.Actions;
using Circus.MarketData;
using Circus.Sequencing;
using Circus.Sessions;
using Circus.Simulator;
using NUnit.Framework;

namespace Circus.Tests.MarketData;

// The whole outbound path, end to end: a sequencer decides what order things happened in, and a
// channel publishes market data for them in that order. Everything either side of that is tested
// on its own; what is here is that the two compose, and that the ordering a channel promises is
// the ordering the venue actually dispatched.
[TestFixture]
public class ChannelFromDispatchTests
{
    private static readonly DateTime Day = new(2000, 1, 1);

    private static readonly Security Gold = new("GCZ6", SecurityType.Future, 10, 10);
    private static readonly Security Silver = new("SIZ6", SecurityType.Future, 10, 10);

    // Open before the trace starts and closed long after it ends, so the books actually trade
    // for the whole of it. A schedule with boundaries inside the trace would leave most of the
    // flow arriving at a closed book and being rejected - and rejections still produce market
    // data, so the test would pass while measuring almost nothing.
    private static MarketSchedule OpenThroughout() =>
        new(new TimeSpan(8, 0, 0), new TimeSpan(8, 30, 0), new TimeSpan(17, 0, 0));

    [Test]
    public void MarketDataFollowsTheOrderTheVenueDispatchedIn()
    {
        // arrange
        var sequencer = new Sequencer(Day);
        sequencer.Add(new OrderBook(Gold), OpenThroughout());
        sequencer.Add(new OrderBook(Silver), OpenThroughout());

        var channel = new MarketDataChannel();
        channel.Add(new SecurityFeed(Gold, maxLevels: 10));
        channel.Add(new SecurityFeed(Silver, maxLevels: 10));

        var trace = new OrderFlowSimulator(new[] {Gold, Silver}, seed: 21).Generate(400);

        // act - publish each dispatch's events as they come out
        var published = new List<ChannelMessage>();
        Replay.Run(sequencer, trace, d => published.AddRange(channel.Publish(d.Events)));

        // assert
        Assert.IsNotEmpty(published);

        // one contiguous run of numbers, so a subscriber counting them can tell loss from silence
        Assert.AreEqual(
            Enumerable.Range(1, published.Count).Select(i => (long) i).ToList(),
            published.Select(m => m.Sequence).ToList());

        // both instruments on the one channel, each message saying which
        Assert.AreEqual(
            new[] {Gold.Name, Silver.Name},
            published.Select(m => m.Data.Security.Name).Distinct().OrderBy(n => n).ToArray());

        // and time never runs backwards along the channel, because the venue's dispatch order is
        // what put the messages there
        var times = published.Select(m => m.Data.Time).ToList();
        Assert.AreEqual(times.OrderBy(t => t).ToList(), times);
    }

    [Test]
    public void TwoChannelsSplittingTheVenue_EachNumberItsOwnMessagesFromOne()
    {
        // arrange - one channel per instrument, which is how a venue actually splits its feeds
        var sequencer = new Sequencer(Day);
        sequencer.Add(new OrderBook(Gold), OpenThroughout());
        sequencer.Add(new OrderBook(Silver), OpenThroughout());

        var goldChannel = new MarketDataChannel();
        goldChannel.Add(new SecurityFeed(Gold, maxLevels: 10));

        var silverChannel = new MarketDataChannel();
        silverChannel.Add(new SecurityFeed(Silver, maxLevels: 10));

        var trace = new OrderFlowSimulator(new[] {Gold, Silver}, seed: 22).Generate(400);

        // act
        var goldMessages = new List<ChannelMessage>();
        var silverMessages = new List<ChannelMessage>();
        Replay.Run(sequencer, trace, d =>
        {
            goldMessages.AddRange(goldChannel.Publish(d.Events));
            silverMessages.AddRange(silverChannel.Publish(d.Events));
        });

        // assert - neither channel's numbering has a hole where the other's instrument traded.
        // This is why the sequence is the channel's own count rather than the venue's dispatch
        // count: a subscriber to one instrument would otherwise see gaps constantly and never be
        // able to tell one from a lost message.
        Assert.IsNotEmpty(goldMessages);
        Assert.IsNotEmpty(silverMessages);

        Assert.AreEqual(
            Enumerable.Range(1, goldMessages.Count).Select(i => (long) i).ToList(),
            goldMessages.Select(m => m.Sequence).ToList());
        Assert.AreEqual(
            Enumerable.Range(1, silverMessages.Count).Select(i => (long) i).ToList(),
            silverMessages.Select(m => m.Sequence).ToList());

        // each carrying only its own instrument
        Assert.IsTrue(goldMessages.All(m => m.Data.Security.Name == Gold.Name));
        Assert.IsTrue(silverMessages.All(m => m.Data.Security.Name == Silver.Name));
    }

    [Test]
    public void ReplayingTheSameTrace_PublishesTheSameMessages()
    {
        var trace = new OrderFlowSimulator(new[] {Gold, Silver}, seed: 23).Generate(400);

        var first = PublishAll(trace);
        var second = PublishAll(trace);

        // market data is a function of the dispatch stream, which is a function of the trace -
        // so a feed can be rebuilt from a journal rather than having to have been recorded
        Assert.AreEqual(first, second);
        Assert.IsNotEmpty(first);
    }

    private static List<string> PublishAll(IReadOnlyList<OrderBookAction> trace)
    {
        var sequencer = new Sequencer(Day);
        sequencer.Add(new OrderBook(Gold), OpenThroughout());
        sequencer.Add(new OrderBook(Silver), OpenThroughout());

        var channel = new MarketDataChannel();
        channel.Add(new SecurityFeed(Gold, maxLevels: 10));
        channel.Add(new SecurityFeed(Silver, maxLevels: 10));

        var rendered = new List<string>();
        Replay.Run(sequencer, trace, d =>
        {
            foreach (var message in channel.Publish(d.Events))
                rendered.Add($"{message.Sequence} {message.Data.Security.Name} {Describe(message.Data)}");
        });

        return rendered;
    }

    // Rendered rather than compared directly: LevelsDataEvent holds its ladders in lists, and a
    // record's generated equality compares those by reference.
    private static string Describe(MarketDataEvent data) => data switch
    {
        LevelsDataEvent levels =>
            $"Levels {levels.Time:O} [{string.Join(",", levels.Bids)}] [{string.Join(",", levels.Offers)}]",
        _ => data.ToString()!
    };
}
