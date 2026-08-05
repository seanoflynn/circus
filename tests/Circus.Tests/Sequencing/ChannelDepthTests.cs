using Circus.Actions;
using Circus.MarketData;
using Circus.Sequencing;
using Circus.Sessions;
using Circus.Tests.Helpers;
using NUnit.Framework;

namespace Circus.Tests.Sequencing;

// How deep a channel's by-price products run, declared per channel rather than per book. A venue
// publishing the same instrument at two depths is ordinary - CME runs a top-of-book channel
// beside its ten-deep one, Databento sells mbp-1 and mbp-10 off the same book - and this is what
// makes that expressible.
//
// The group is what makes it work, because the two halves cannot be configured apart: a shallow
// delta stream has to be diffed at its own window (see PublishedDepthTests), so declaring a
// five-deep channel has to reach back and tell the book to report at five. Doing that by hand is
// the mistake this class exists to make impossible.
public class ChannelDepthTests
{
    private static readonly Instrument Gold = new("GCZ6", 10, 10);
    private static readonly DateTime Day = new(2000, 1, 3, 8, 0, 0);

    // Out of the way of a run that starts at 08:00, so no session boundary lands inside it.
    private static readonly MarketSchedule OutOfTheWay =
        new(new TimeSpan(22, 0, 0), new TimeSpan(22, 30, 0), new TimeSpan(23, 30, 0));

    // Rests three bids at descending prices, then a fourth that becomes the best - so a one-deep
    // channel sees the old best leave its window while a ten-deep one sees only the arrival.
    private static Dictionary<string, List<ChannelMessage>> BuildAndTopTheBook(InstrumentGroup group)
    {
        var published = group.ChannelNames.ToDictionary(n => n, _ => new List<ChannelMessage>());

        group.Submit(new OpenTrading {Symbol = Gold.Symbol, Time = Day.AddMinutes(1)});

        var prices = new[] {200m, 190m, 180m, 210m};
        for (var i = 0; i < prices.Length; i++)
        {
            group.Submit(new CreateLimitOrder
            {
                Symbol = Gold.Symbol, Time = Day.AddMinutes(45).AddSeconds(i), CompanyId = "C1",
                ClientOrderId = $"O{i}", OrderValidity = new OrderValidity.Day(), Side = Side.Buy,
                Quantity = 1, Price = prices[i]
            });
        }

        foreach (var dispatched in group.Sequencer.AdvanceTo(Day.AddHours(1)))
        {
            foreach (var name in group.ChannelNames)
                published[name].AddRange(group.ChannelNamed(name).Publish(dispatched.Events));
        }

        return published;
    }

    private static MarketByPriceDeltaEvent LastDelta(IEnumerable<ChannelMessage> messages) =>
        messages.Select(m => m.Data).OfType<MarketByPriceDeltaEvent>().Last();

    [Test]
    public void AChannel_PublishesAtTheDepthItWasDeclaredWith()
    {
        var group = new InstrumentGroup(Day);
        group.AddChannel("tob", FeedProducts.ByPrice, depth: 1);
        group.AddChannel("deep", FeedProducts.ByPrice, depth: 10);
        group.Add(Gold, OutOfTheWay);

        var published = BuildAndTopTheBook(group);

        Assert.AreEqual(1, LastDelta(published["tob"]).Depth);
        Assert.AreEqual(10, LastDelta(published["deep"]).Depth);
    }

    // The payoff, and the reason depth could not simply be a filter on the way out. The same
    // action is one message on each channel, and they say different things: ten deep the old best
    // bid is still published and unchanged, one deep it is gone.
    [Test]
    public void AShallowChannel_IsToldWhenALevelLeavesItsWindow()
    {
        var group = new InstrumentGroup(Day);
        group.AddChannel("tob", FeedProducts.ByPrice, depth: 1);
        group.AddChannel("deep", FeedProducts.ByPrice, depth: 10);
        group.Add(Gold, OutOfTheWay);

        var published = BuildAndTopTheBook(group);

        Assert.AreEqual(new[] {(MarketByPriceDeltaAction.Added, 210m)},
            LastDelta(published["deep"]).Changes.Select(c => (c.Action, c.Price)).ToArray(),
            "ten deep, the levels beneath the new best only moved rank, which is not news");

        Assert.AreEqual(
            new[] {(MarketByPriceDeltaAction.Added, 210m), (MarketByPriceDeltaAction.Removed, 200m)},
            LastDelta(published["tob"]).Changes.Select(c => (c.Action, c.Price)).ToArray(),
            "one deep, 200 is no longer published and the channel has to say so");
    }

    // A subscriber applying one channel's messages ends up with that channel's window, which is
    // the property the Removed above exists to give it.
    [Test]
    public void ASubscriberToAShallowChannel_HoldsTheShallowBook()
    {
        var group = new InstrumentGroup(Day);
        group.AddChannel("tob", FeedProducts.ByPrice, depth: 1);
        group.AddChannel("deep", FeedProducts.ByPrice, depth: 10);
        group.Add(Gold, OutOfTheWay);

        var published = BuildAndTopTheBook(group);

        Assert.AreEqual(new[] {210m}, Replay(published["tob"]).Bids.Select(b => b.Price).ToArray());
        Assert.AreEqual(new[] {210m, 200m, 190m, 180m},
            Replay(published["deep"]).Bids.Select(b => b.Price).ToArray());
    }

    private static LevelBook Replay(IEnumerable<ChannelMessage> messages)
    {
        var book = new LevelBook();

        foreach (var message in messages)
        {
            if (message.Data is MarketByPriceDeltaEvent delta)
                book.Apply(delta);
        }

        return book;
    }

    // The book is built from its channels' depths, so a group declaring one shallow channel does
    // not leave a ten-deep book diffing a window nobody reads - and, more to the point, cannot
    // leave a channel reading a window the book never diffed.
    [Test]
    public void ABookIsBuilt_ToReportEveryDepthItsChannelsPublish()
    {
        var group = new InstrumentGroup(Day);
        group.AddChannel("tob", FeedProducts.ByPrice, depth: 1);
        group.AddChannel("deep", FeedProducts.ByPrice, depth: 5);
        group.AddChannel("orders", FeedProducts.ByOrder, depth: 20);
        group.Add(Gold, OutOfTheWay);

        Assert.AreEqual(new[] {1, 5}, group.PublishedDepthsFor(Gold.Symbol).ToArray(),
            "an order-by-order channel carries no by-price product, so its depth means nothing");
    }

    // Declaring channels and adding instruments still commute, which they only can if a late
    // channel can reach the books that are already here.
    [Test]
    public void AChannelDeclaredAfterAnInstrument_StillGetsItsOwnDepth()
    {
        var group = new InstrumentGroup(Day);
        group.AddChannel("deep", FeedProducts.ByPrice, depth: 10);
        group.Add(Gold, OutOfTheWay);
        group.AddChannel("tob", FeedProducts.ByPrice, depth: 1);

        var published = BuildAndTopTheBook(group);

        Assert.AreEqual(
            new[] {(MarketByPriceDeltaAction.Added, 210m), (MarketByPriceDeltaAction.Removed, 200m)},
            LastDelta(published["tob"]).Changes.Select(c => (c.Action, c.Price)).ToArray());
    }

    [Test]
    public void ASnapshot_CarriesTheChannelsOwnDepth()
    {
        var group = new InstrumentGroup(Day, TimeSpan.FromMinutes(10));
        group.AddChannel("tob", FeedProducts.ByPrice, depth: 1);
        group.AddChannel("deep", FeedProducts.ByPrice, depth: 10);
        group.Add(Gold, OutOfTheWay);

        var published = BuildAndTopTheBook(group);

        var top = published["tob"].Select(m => m.Data).OfType<LevelsDataEvent>().Last();
        var deep = published["deep"].Select(m => m.Data).OfType<LevelsDataEvent>().Last();

        Assert.AreEqual(1, top.Depth);
        Assert.AreEqual(new[] {210m}, top.Bids.Select(b => b.Price).ToArray(),
            "an image truncates cleanly, unlike a delta");
        Assert.AreEqual(10, deep.Depth);
        Assert.AreEqual(new[] {210m, 200m, 190m, 180m}, deep.Bids.Select(b => b.Price).ToArray());
    }

    [Test]
    public void AChannelCarryingNoLevels_IsRefused()
    {
        var group = new InstrumentGroup(Day);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => group.AddChannel("nothing", FeedProducts.ByPrice, depth: 0));
    }
}
