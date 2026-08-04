using Circus.Events;
using Circus.MarketData;
using Circus.Time;
using NUnit.Framework;

namespace Circus.Tests.MarketData;

// A channel carries several instruments under one sequence. The sequence is the whole reason it
// exists as a type rather than a list of feeds, so most of what is here is about that: it counts
// this channel's own messages, contiguously, so a subscriber seeing it skip knows it lost one.
[TestFixture]
public class MarketDataChannelTests
{
    private static readonly Instrument Gold = new("GCZ6", 10, 10);
    private static readonly Instrument Silver = new("SIZ6", 10, 10);
    private static readonly DateTime Now1 = new(2000, 1, 1, 12, 0, 0);

    [Test]
    public void Publish_NumbersMessagesContiguouslyFromOne()
    {
        // arrange
        var channel = Channel(Gold, Silver);
        var gold = Book(Gold);
        var silver = Book(Silver);

        // act
        var messages = new List<ChannelMessage>();
        messages.AddRange(channel.Publish(gold.UpdateStatus(OrderBookStatus.Open)));
        messages.AddRange(channel.Publish(silver.UpdateStatus(OrderBookStatus.Open)));
        messages.AddRange(channel.Publish(
            gold.CreateLimitOrder("Company1", "Order1", new OrderValidity.Day(), Side.Buy, 3, 100)));

        // assert
        Assert.IsNotEmpty(messages);
        Assert.AreEqual(
            Enumerable.Range(1, messages.Count).Select(i => (long) i).ToList(),
            messages.Select(m => m.Sequence).ToList());
        Assert.AreEqual(messages.Count, channel.Sequence);
    }

    [Test]
    public void Publish_CarriesBothInstrumentsAndSaysWhichIsWhich()
    {
        // arrange
        var channel = Channel(Gold, Silver);
        var gold = Book(Gold);
        var silver = Book(Silver);

        // act
        var messages = new List<ChannelMessage>();
        messages.AddRange(channel.Publish(gold.UpdateStatus(OrderBookStatus.Open)));
        messages.AddRange(channel.Publish(silver.UpdateStatus(OrderBookStatus.Open)));

        // assert - filtered to the status messages, since a level producer republishes its
        // ladders on any non-empty batch and so contributes one of its own here too
        Assert.AreEqual(
            new[] {Gold.Symbol, Silver.Symbol},
            messages.Where(m => m.Data is InstrumentStatusDataEvent)
                .Select(m => m.Data.Symbol).ToArray());
    }

    [Test]
    public void Publish_AnInstrumentTheChannelDoesNotCarry_IsIgnoredAndDoesNotConsumeASequence()
    {
        // arrange - a channel carrying gold only, which is the normal case rather than a mistake:
        // a venue splits its instruments across channels deliberately
        var channel = Channel(Gold);
        var gold = Book(Gold);
        var silver = Book(Silver);

        // act
        var first = channel.Publish(gold.UpdateStatus(OrderBookStatus.Open));
        var ignored = channel.Publish(silver.UpdateStatus(OrderBookStatus.Open));
        var second = channel.Publish(
            gold.CreateLimitOrder("Company1", "Order1", new OrderValidity.Day(), Side.Buy, 3, 100));

        // assert - silver contributed nothing, and crucially left no hole in gold's numbering.
        // Had the channel carried the venue's own dispatch count instead of its own, a subscriber
        // could never have told a filtered-out instrument from a lost message.
        Assert.IsEmpty(ignored);
        Assert.AreEqual(
            Enumerable.Range(1, first.Count + second.Count).Select(i => (long) i).ToList(),
            first.Concat(second).Select(m => m.Sequence).ToList());
    }

    [Test]
    public void Publish_EventsSpanningInstruments_ReachEachInstrumentsOwnFeed()
    {
        // arrange - nothing produces this today, since one dispatch is one book. It is what an
        // action implying a fill in another book would look like, and each book's producers must
        // be handed their own book's events and only those.
        var channel = Channel(Gold, Silver);

        var mixed = new OrderBookEvent[]
        {
            new StatusChanged(Gold.Symbol, Now1, OrderBookStatus.Open),
            new StatusChanged(Silver.Symbol, Now1, OrderBookStatus.Halted),
            new StatusChanged(Gold.Symbol, Now1, OrderBookStatus.Closed)
        };

        // act
        var messages = channel.Publish(mixed);

        // assert - grouped by instrument in order of first appearance, and each feed saw only its
        // own: gold's status producer tracked two changes, silver's one
        var statuses = messages
            .Select(m => m.Data)
            .OfType<InstrumentStatusDataEvent>()
            .Select(d => (d.Symbol, d.Status))
            .ToList();

        Assert.AreEqual(
            new[]
            {
                (Gold.Symbol, OrderBookStatus.Open),
                (Gold.Symbol, OrderBookStatus.Closed),
                (Silver.Symbol, OrderBookStatus.Halted)
            },
            statuses);
    }

    [Test]
    public void Publish_NoEvents_ProducesNothing()
    {
        var channel = Channel(Gold);

        Assert.IsEmpty(channel.Publish(Array.Empty<OrderBookEvent>()));
        Assert.AreEqual(0, channel.Sequence);
    }

    [Test]
    public void Add_TwiceForTheSameInstrument_ArgumentException()
    {
        var channel = Channel(Gold);

        Assert.Throws<ArgumentException>(() => channel.Add(new InstrumentFeed(Gold.Symbol)));
    }

    private static MarketDataChannel Channel(params Instrument[] instruments)
    {
        var channel = new MarketDataChannel();
        foreach (var instrument in instruments)
            channel.Add(new InstrumentFeed(instrument.Symbol));

        return channel;
    }

    private static IOrderBook Book(Instrument instrument) =>
        new TimestampingOrderBook(instrument, new ManualClock(Now1));
}