using Circus.Actions;
using Circus.MarketData;
using Circus.Sequencing;
using Circus.Sessions;
using Circus.Tests.Helpers;
using NUnit.Framework;

namespace Circus.Tests.Sequencing;

// One sequencer, several channels. The dispatch order is the venue's and there is one of it; what
// gets published about it is a product decision with several answers.
//
// CME runs a channel per product group carrying by-price and by-order together. Eurex publishes
// the same instrument on EOBI and on EMDI, with by-order on one and by-price on the other and
// state on both. Both are this class with different channels declared, which is what step 4 is
// for - so the Eurex shape gets a test rather than a comment claiming it would work.
public class MultipleChannelTests
{
    private static readonly Instrument Gold = new("GCZ6", 10, 10);
    private static readonly Instrument Silver = new("SIZ6", 10, 10);
    private static readonly DateTime Day = new(2000, 1, 3, 8, 0, 0);

    private static MarketSchedule OpenThroughout() =>
        new(new TimeSpan(8, 30, 0), new TimeSpan(9, 0, 0), new TimeSpan(23, 0, 0));

    // Drives a trade and returns what each channel published for it. A trade moves every product
    // at once, so what a channel carries is decided by its products rather than by the action.
    private static Dictionary<string, List<ChannelMessage>> Trade(InstrumentGroup group)
    {
        var published = group.ChannelNames.ToDictionary(n => n, _ => new List<ChannelMessage>());

        group.Submit(new OpenTrading {Symbol = Gold.Symbol, Time = Day.AddHours(1)});
        group.Submit(Order("C1", Side.Buy, 3, 100, Day.AddHours(1).AddSeconds(1)));
        group.Submit(Order("C2", Side.Sell, 3, 100, Day.AddHours(1).AddSeconds(2)));

        foreach (var dispatched in group.Sequencer.AdvanceTo(Day.AddHours(2)))
        {
            foreach (var name in group.ChannelNames)
                published[name].AddRange(group.ChannelNamed(name).Publish(dispatched.Events));
        }

        return published;
    }

    private static CreateLimitOrder Order(string companyId, Side side, int quantity, decimal price,
        DateTime time) =>
        new()
        {
            Symbol = Gold.Symbol, Time = time, CompanyId = companyId, ClientOrderId = $"O-{companyId}",
            OrderValidity = new OrderValidity.Day(), Side = side, Quantity = quantity, Price = price
        };

    // Sorted, because which products a channel carries is what these are about - the order a feed
    // emits them in is InstrumentFeedTests' subject, and asserting it here would couple these to
    // something they do not care about.
    private static string[] Kinds(IEnumerable<ChannelMessage> messages) =>
        messages.Select(m => m.Data.GetType().Name).Distinct().OrderBy(n => n).ToArray();

    [Test]
    public void AGroupThatDeclaresNoChannel_GetsOneCarryingEverything()
    {
        var group = new InstrumentGroup(Day);
        group.Add(Gold, OpenThroughout());

        Assert.AreEqual(new[] {MarketDataChannel.DefaultName}, group.ChannelNames.ToArray());
        Assert.AreEqual(new[]
            {
                nameof(InstrumentStatusDataEvent), nameof(MarketByOrderDeltaEvent),
                nameof(MarketByPriceDeltaEvent), nameof(TradeDataEvent)
            },
            Kinds(Trade(group)[MarketDataChannel.DefaultName]));
    }

    // The Eurex shape: one instrument, two channels, different products on each and state on both.
    [Test]
    public void OneInstrument_CanBePublishedOnTwoChannelsCarryingDifferentProducts()
    {
        var group = new InstrumentGroup(Day);
        group.AddChannel("EOBI", FeedProducts.ByOrder | FeedProducts.Status);
        group.AddChannel("EMDI", FeedProducts.ByPrice | FeedProducts.Trades | FeedProducts.Status);
        group.Add(Gold, OpenThroughout());

        var published = Trade(group);

        Assert.AreEqual(new[] {nameof(InstrumentStatusDataEvent), nameof(MarketByOrderDeltaEvent)},
            Kinds(published["EOBI"]));
        Assert.AreEqual(new[]
            {
                nameof(InstrumentStatusDataEvent), nameof(MarketByPriceDeltaEvent), nameof(TradeDataEvent)
            },
            Kinds(published["EMDI"]));
    }

    // Each channel counts its own messages. A subscriber to one must see a contiguous run, which
    // it would not if the two shared a numbering and each dropped what the other carried.
    [Test]
    public void EachChannel_NumbersItsOwnMessages()
    {
        var group = new InstrumentGroup(Day);
        group.AddChannel("EOBI", FeedProducts.ByOrder);
        group.AddChannel("EMDI", FeedProducts.ByPrice | FeedProducts.Trades);
        group.Add(Gold, OpenThroughout());

        var published = Trade(group);

        foreach (var (name, messages) in published)
        {
            Assert.IsNotEmpty(messages, $"{name} published nothing");
            Assert.AreEqual(Enumerable.Range(1, messages.Count).Select(i => (long) i).ToArray(),
                messages.Select(m => m.Sequence).ToArray(),
                $"{name} must number its own messages from one, with no gap");
        }
    }

    [Test]
    public void AChannelDeclaredAfterAnInstrument_StillCarriesIt()
    {
        var group = new InstrumentGroup(Day);
        group.Add(Gold, OpenThroughout());
        group.AddChannel("late", FeedProducts.ByPrice);

        var published = Trade(group);

        Assert.IsNotEmpty(published["late"],
            "declaring channels and adding instruments should commute");
    }

    [Test]
    public void AnInstrument_CanBeHeldBackFromAChannel()
    {
        var group = new InstrumentGroup(Day);
        group.AddChannel("majors");
        group.AddChannel("minors");
        group.Add(Gold, OpenThroughout(), channels: new[] {"majors"});
        group.Add(Silver, OpenThroughout(), channels: new[] {"minors"});

        Assert.AreEqual(new[] {Gold.Symbol}, group.ChannelNamed("majors").Symbols.ToArray());
        Assert.AreEqual(new[] {Silver.Symbol}, group.ChannelNamed("minors").Symbols.ToArray());
    }

    [Test]
    public void NamingAChannelThatDoesNotExist_IsRefused()
    {
        var group = new InstrumentGroup(Day);
        group.AddChannel("real");

        Assert.Throws<ArgumentException>(
            () => group.Add(Gold, OpenThroughout(), channels: new[] {"imaginary"}));
    }

    [Test]
    public void DeclaringTheSameChannelTwice_IsRefused()
    {
        var group = new InstrumentGroup(Day);
        group.AddChannel("one");

        Assert.Throws<ArgumentException>(() => group.AddChannel("one"));
    }

    // There is no single channel to take once there are several, and picking one would hand back
    // a stream the caller did not ask for.
    [Test]
    public void TheSingleChannelShortcut_IsRefusedWhenThereAreSeveral()
    {
        var group = new InstrumentGroup(Day);
        group.AddChannel("a");
        group.AddChannel("b");
        group.Add(Gold, OpenThroughout());

        Assert.Throws<InvalidOperationException>(() => _ = group.Channel);
        Assert.IsNotNull(group.ChannelNamed("a"));
    }

    [Test]
    public void TheSingleChannelShortcut_IsRefusedWhenThereAreNone()
    {
        Assert.Throws<InvalidOperationException>(() => _ = new InstrumentGroup(Day).Channel);
    }
}
