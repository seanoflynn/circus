using Circus.Actions;
using Circus.MarketData;
using Circus.Sequencing;
using Circus.Sessions;
using Circus.Simulator;
using NUnit.Framework;

namespace Circus.Tests.Sequencing;

// InstrumentGroup bundles registration of a book and its market-data feed into one step.
// What is here is that the two registrations stay in sync, and that the convenience methods
// produce the same result the manual wiring would have.
[TestFixture]
public class InstrumentGroupTests
{
    private static readonly DateTime Day = new(2000, 1, 1);

    private static readonly Instrument Gold = new("GCZ6", 10, 10);
    private static readonly Instrument Silver = new("SIZ6", 10, 10);

    // A book that pauses when it trades through 105 (5 ticks from reference 100).
    private static readonly Instrument PausingGold = new("GCZ6", 10, 10,
        PriceRestrictions: new PriceRestriction[] {new VolatilityBand(5, TimeSpan.FromMinutes(2))});

    private static MarketSchedule OpenThroughout() =>
        new(new TimeSpan(8, 0, 0), new TimeSpan(8, 30, 0), new TimeSpan(17, 0, 0));

    private static MarketSchedule Quiet() =>
        new(new TimeSpan(23, 0, 0), new TimeSpan(23, 15, 0), new TimeSpan(23, 45, 0));

    private static DateTime At(int hour, int minute) => Day.Add(new TimeSpan(hour, minute, 0));

    [Test]
    public void Add_RegistersBookAndFeed()
    {
        var group = new InstrumentGroup(Day);
        group.Add(Gold, OpenThroughout());

        group.Submit(new OpenTrading {Symbol = Gold.Symbol, Time = At(9, 0)});
        var dispatched = group.Sequencer.AdvanceTo(At(10, 0));
        var messages = group.Channel.Publish(dispatched.SelectMany(d => d.Events).ToList());

        Assert.IsNotEmpty(messages);
        Assert.AreEqual(1, messages[0].Sequence);
        Assert.AreEqual(
            Enumerable.Range(1, messages.Count).Select(i => (long) i).ToList(),
            messages.Select(m => m.Sequence).ToList());
    }

    [Test]
    public void TwoInstrumentsShareOneSequence()
    {
        var group = new InstrumentGroup(Day);
        group.Add(Gold, OpenThroughout());
        group.Add(Silver, OpenThroughout());

        group.Submit(new OpenTrading {Symbol = Gold.Symbol, Time = At(9, 0)});
        group.Submit(new OpenTrading {Symbol = Silver.Symbol, Time = At(9, 0)});
        var dispatched = group.Sequencer.AdvanceTo(At(10, 0));

        var messages = new List<ChannelMessage>();
        foreach (var d in dispatched)
            messages.AddRange(group.Channel.Publish(d.Events));

        Assert.IsNotEmpty(messages);
        Assert.AreEqual(
            Enumerable.Range(1, messages.Count).Select(i => (long) i).ToList(),
            messages.Select(m => m.Sequence).ToList());

        // both symbols appear
        Assert.AreEqual(
            new[] {Gold.Symbol, Silver.Symbol},
            messages.Select(m => m.Data.Symbol).Distinct().OrderBy(n => n).ToArray());
    }

    [Test]
    public void Add_IOrderBook_AcceptsPreBuiltBook()
    {
        var group = new InstrumentGroup(Day);
        group.Add(new OrderBook(PausingGold), OpenThroughout());

        group.Submit(new OpenTrading {Symbol = PausingGold.Symbol, Time = At(9, 0), ReferencePrice = 100});
        group.Sequencer.AdvanceTo(At(10, 0));

        Assert.That(group.Symbols, Has.Member(PausingGold.Symbol));
    }

    [Test]
    public void Add_TwiceForSameSymbol_Throws()
    {
        var group = new InstrumentGroup(Day);
        group.Add(Gold, OpenThroughout());

        Assert.Throws<ArgumentException>(() => group.Add(Gold, OpenThroughout()));
    }

    [Test]
    public void Submit_UnknownSymbol_Throws()
    {
        var group = new InstrumentGroup(Day);

        Assert.Throws<ArgumentException>(() =>
            group.Submit(new OpenTrading {Symbol = "UNKNOWN", Time = At(9, 0)}));
    }

    [Test]
    public void ExposesSequencerAndChannel()
    {
        var group = new InstrumentGroup(Day);

        Assert.IsNotNull(group.Sequencer);
        Assert.IsNotNull(group.Channel);
        Assert.AreEqual(Day, group.Sequencer.LogicalNow);
    }

    [Test]
    public void Replay_Run_Convenience_ProducesSameResultAsManualWiring()
    {
        var trace = new OrderFlowSimulator(new[] {Gold, Silver}, seed: 42).Generate(200);

        // Manual wiring
        var manual = new InstrumentGroup(Day);
        manual.Add(Gold, OpenThroughout());
        manual.Add(Silver, OpenThroughout());

        var manualMessages = new List<ChannelMessage>();
        Replay.Run(manual.Sequencer, trace,
            d => manualMessages.AddRange(manual.Channel.Publish(d.Events)));

        // Convenience
        var convenience = new InstrumentGroup(Day);
        convenience.Add(Gold, OpenThroughout());
        convenience.Add(Silver, OpenThroughout());

        var convenienceMessages = Replay.Run(convenience, trace);

        // assert
        Assert.IsNotEmpty(manualMessages);
        Assert.IsNotEmpty(convenienceMessages);
        Assert.AreEqual(manualMessages.Count, convenienceMessages.Count);
        Assert.AreEqual(
            manualMessages.Select(m => m.Sequence).ToList(),
            convenienceMessages.Select(m => m.Sequence).ToList());
    }
}