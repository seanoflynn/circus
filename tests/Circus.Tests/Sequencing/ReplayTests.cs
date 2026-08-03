using Circus.Actions;
using Circus.Events;
using Circus.Sequencing;
using Circus.Sessions;
using Circus.Simulator;
using Circus.Tests.Helpers;
using NUnit.Framework;

namespace Circus.Tests.Sequencing;

// Replay feeds a recorded trace through a sequencer with no clock involved. Two things are worth
// holding: that feeding it action by action produces the same dispatch order as submitting the
// whole thing up front, since that equivalence is the only reason it is allowed to stream; and
// that a replay of a trace reproduces a run rather than resembling it.
[TestFixture]
public class ReplayTests
{
    private static readonly DateTime Day = new(2000, 1, 1);

    private static readonly Instrument Gold = new("GCZ6", 10, 10);
    private static readonly Instrument Silver = new("SIZ6", 10, 10);

    private static MarketSchedule TradingDay() => new(new(9, 0, 0), new(9, 30, 0), new(17, 0, 0));

    // Boundaries late enough that a trace running from midday never reaches them.
    private static MarketSchedule Quiet() => new(new(23, 0, 0), new(23, 15, 0), new(23, 45, 0));

    private static DateTime At(int hour, int minute) => Day.Add(new TimeSpan(hour, minute, 0));

    [Test]
    public void Run_DispatchesTheWholeTraceInOrder()
    {
        // arrange
        var book = new OrderBook(Gold);
        var sequencer = new Sequencer(At(12, 0));
        sequencer.Add(book, Quiet());

        var trace = new OrderBookAction[]
        {
            new OpenTrading {Symbol = Gold.Symbol, Time = At(12, 0)},
            Order(Gold, "Sell1", Side.Sell, 100, At(12, 1)),
            Order(Gold, "Buy1", Side.Buy, 100, At(12, 2))
        };

        // act
        var dispatched = new List<Dispatched>();
        Replay.Run(sequencer, trace, dispatched.Add);

        // assert - every action, in the order recorded, and the book actually traded
        Assert.AreEqual(3, dispatched.Count);
        Assert.AreEqual(new long[] {1, 2, 3}, dispatched.Select(d => d.Sequence).ToArray());
        Assert.AreEqual(1, dispatched.SelectMany(d => d.Events).Trades().Count());
    }

    [Test]
    public void Run_StreamedActionByAction_MatchesSubmittingTheWholeTraceUpFront()
    {
        // arrange - a trace long enough, and mixed enough, that a difference in tie-breaking
        // would show up somewhere in it
        var trace = new OrderFlowSimulator(new[] {Gold, Silver}, seed: 99).Generate(400);

        // act
        var streamed = new List<string>();
        var streamedSequencer = Venue();
        Replay.Run(streamedSequencer, trace, d => streamed.Add(Describe(d)));

        var upFront = new List<string>();
        var upFrontSequencer = Venue();
        foreach (var action in trace)
            upFrontSequencer.Submit(action);
        foreach (var d in upFrontSequencer.AdvanceTo(trace[^1].Time))
            upFront.Add(Describe(d));

        // assert - this is what lets Replay stream rather than hold the whole trace in the queue.
        // Ties are settled by kind before the submission counter, so client flow never reorders
        // against a schedule transition however the counters fall, and two entries of one kind
        // keep their relative counters however many others were queued between them.
        Assert.AreEqual(upFront, streamed);
        Assert.IsNotEmpty(streamed);
    }

    [Test]
    public void Run_TwiceOverTheSameTrace_ReproducesItExactly()
    {
        // arrange
        var trace = new OrderFlowSimulator(new[] {Gold, Silver}, seed: 4).Generate(400);

        // act
        var first = new List<string>();
        Replay.Run(Venue(), trace, d => first.Add(Describe(d)));

        var second = new List<string>();
        Replay.Run(Venue(), trace, d => second.Add(Describe(d)));

        // assert - the same events, timestamps included, which is what a journal of the trace
        // would be worth
        Assert.AreEqual(first, second);
        Assert.IsNotEmpty(first);
    }

    [Test]
    public void Run_Until_DispatchesTheCloseTheTraceStoppedShortOf()
    {
        // arrange - a trace that ends mid-session, on a book whose day closes at 17:00
        var book = new OrderBook(Gold);
        var sequencer = new Sequencer(Day);
        sequencer.Add(book, TradingDay());

        var trace = new OrderBookAction[] {Order(Gold, "Buy1", Side.Buy, 100, At(10, 0))};

        // act
        var dispatched = new List<Dispatched>();
        Replay.Run(sequencer, trace, dispatched.Add, until: At(18, 0));

        // assert - pre-open and open came due before the order, the close after it
        Assert.AreEqual(
            new[]
            {
                nameof(PreOpenTrading), nameof(OpenTrading), nameof(CreateLimitOrder),
                nameof(CloseTrading)
            },
            dispatched.Select(d => d.Action.GetType().Name).ToArray());
        Assert.AreEqual(OrderBookStatus.Closed, book.Status);
    }

    [Test]
    public void Run_UntilBehindWhereTheTraceEnded_IsIgnoredRatherThanThrowing()
    {
        // arrange
        var book = new OrderBook(Gold);
        var sequencer = new Sequencer(At(12, 0));
        sequencer.Add(book, Quiet());

        var trace = new OrderBookAction[]
        {
            new OpenTrading {Symbol = Gold.Symbol, Time = At(12, 0)},
            Order(Gold, "Buy1", Side.Buy, 100, At(14, 0))
        };

        // act - asking to finish somewhere the trace has already gone past
        Assert.DoesNotThrow(() => Replay.Run(sequencer, trace, until: At(13, 0)));

        // assert - left where the trace left it, not wound back
        Assert.AreEqual(At(14, 0), sequencer.LogicalNow);
    }

    [Test]
    public void Run_NoTrace_LeavesTheSequencerAlone()
    {
        // arrange
        var sequencer = new Sequencer(At(12, 0));
        sequencer.Add(new OrderBook(Gold), Quiet());

        // act
        var dispatched = new List<Dispatched>();
        Replay.Run(sequencer, Array.Empty<OrderBookAction>(), dispatched.Add);

        // assert
        Assert.IsEmpty(dispatched);
        Assert.AreEqual(At(12, 0), sequencer.LogicalNow);
    }

    // A simulator trace steps a millisecond per action, so 400 of them span 400ms and an ordinary
    // day's boundaries would leave only the pre-open falling inside it. Compressed so all three
    // land in the middle of the flow instead: a transition sharing an instant with client flow is
    // the tie the ordering has to get right, and one at the very start barely tests it.
    private static Sequencer Venue()
    {
        var compressed = new MarketSchedule(
            new TimeSpan(0, 0, 9, 0, 50),
            new TimeSpan(0, 0, 9, 0, 150),
            new TimeSpan(0, 0, 9, 0, 300));

        var sequencer = new Sequencer(Day);
        sequencer.Add(new OrderBook(Gold), compressed);
        sequencer.Add(new OrderBook(Silver), compressed);
        return sequencer;
    }

    private static CreateLimitOrder Order(Instrument instrument, string clientOrderId, Side side,
        decimal price, DateTime time) =>
        new()
        {
            Symbol = instrument.Symbol, Time = time, CompanyId = "Company1", ClientOrderId = clientOrderId,
            OrderValidity = new OrderValidity.Day(), Side = side, Quantity = 5, Price = price
        };

    private static string Describe(Dispatched d) =>
        $"{d.Sequence} {d.Action.Symbol} {d.Action.GetType().Name} {d.Action.Time:O} " +
        $"-> {string.Join("|", d.Events.Select(e => e.GetType().Name))}";
}