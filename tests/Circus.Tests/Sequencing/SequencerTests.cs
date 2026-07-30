using Circus.Actions;
using Circus.Events;
using Circus.Sequencing;
using Circus.Sessions;
using NUnit.Framework;

namespace Circus.Tests.Sequencing;

// One book, so what is under test here is the queue rather than the routing: that three sources
// feed one order, that ties at a single instant fall the way a venue needs them to, and that a
// book's interruption comes back as a poke it never had to ask for. SequencerRoutingTests covers
// several books, and needed none of this to change.
[TestFixture]
public class SequencerTests
{
    private static readonly DateTime Day = new(2000, 1, 1);

    private static readonly TimeSpan PreOpenAt = new(9, 0, 0);
    private static readonly TimeSpan OpenAt = new(9, 30, 0);
    private static readonly TimeSpan CloseAt = new(17, 0, 0);

    private static readonly TimeSpan PauseFor = TimeSpan.FromMinutes(2);

    private static readonly Security Sec = new("GCZ6", SecurityType.Future, 10, 10);

    // A 5-tick volatility band on a reference of 100, so a trade at 200 breaches it and pauses the
    // book for two minutes.
    private static readonly Security PausingSec = new("GCZ6", SecurityType.Future, 10, 10,
        PriceRestrictions: new PriceRestrictionConfig[] {new VolatilityBand(5, PauseFor)});

    private static MarketSchedule TradingDay() => new(PreOpenAt, OpenAt, CloseAt);

    // A day that does not begin until late in the evening, so the schedule stays out of the way
    // while a test drives the book itself.
    private static MarketSchedule Quiet() =>
        new(new TimeSpan(23, 0, 0), new TimeSpan(23, 15, 0), new TimeSpan(23, 45, 0));

    private static DateTime At(TimeSpan timeOfDay) => Day.Add(timeOfDay);

    private static DateTime At(int hour, int minute) => Day.Add(new TimeSpan(hour, minute, 0));

    private static CreateLimitOrder Order(Security security, string companyId, string clientOrderId,
        Side side, decimal price, DateTime time) =>
        new()
        {
            Security = security, Time = time, CompanyId = companyId, ClientOrderId = clientOrderId,
            OrderValidity = new OrderValidity.Day(), Side = side, Quantity = 5, Price = price
        };

    // The book paused at 12:01 and due back two minutes later, with the pair that caused it still
    // crossed and unfilled. Everything is submitted before anything is dispatched, the way a
    // replay feeds in a trace, so every client action carries a lower counter than the tick the
    // pause will queue during dispatch.
    private static (Sequencer Sequencer, OrderBook Book) PausedAtNoon()
    {
        var book = new OrderBook(PausingSec);
        var sequencer = new Sequencer(At(12, 0));
        sequencer.Add(book, Quiet());

        sequencer.Submit(new OpenTrading {Security = PausingSec, Time = At(12, 0), ReferencePrice = 100});
        sequencer.Submit(Order(PausingSec, "Company1", "Sell1", Side.Sell, 200, At(12, 1)));
        sequencer.Submit(Order(PausingSec, "Company2", "Buy1", Side.Buy, 200, At(12, 1)));

        return (sequencer, book);
    }

    private static DateTime Deadline => At(12, 1) + PauseFor;

    [Test]
    public void AdvanceTo_DrivesTheBookThroughItsScheduledDay()
    {
        // arrange
        var book = new OrderBook(Sec);
        var sequencer = new Sequencer(Day);
        sequencer.Add(book, TradingDay());

        // act
        var dispatched = sequencer.AdvanceTo(At(10, 0));

        // assert - the schedule became actions, stamped at the boundaries rather than at the
        // instant the caller happened to advance to
        Assert.AreEqual(2, dispatched.Count);
        Assert.IsInstanceOf<PreOpenTrading>(dispatched[0].Action);
        Assert.AreEqual(At(PreOpenAt), dispatched[0].Action.Time);
        Assert.IsInstanceOf<OpenTrading>(dispatched[1].Action);
        Assert.AreEqual(At(OpenAt), dispatched[1].Action.Time);
        Assert.AreEqual(OrderBookStatus.Open, book.Status);
    }

    [Test]
    public void AdvanceTo_QueuesOnlyTheBoundaryAfterTheOneItDispatched()
    {
        // arrange - opened, so the close is the one thing pending
        var book = new OrderBook(Sec);
        var sequencer = new Sequencer(Day);
        sequencer.Add(book, TradingDay());
        sequencer.AdvanceTo(At(10, 0));

        // act - past the close, and past nothing else: tomorrow's pre-open is queued but not due
        var dispatched = sequencer.AdvanceTo(At(18, 0));

        // assert
        Assert.AreEqual(1, dispatched.Count);
        var close = dispatched[0].Action as CloseTrading;
        Assert.IsNotNull(close);
        Assert.AreEqual(At(CloseAt), close.Time);
        Assert.IsTrue(close.EndsTradingDay, "a lone session is also the day's last");
        Assert.AreEqual(OrderBookStatus.Closed, book.Status);
    }

    [Test]
    public void AdvanceTo_TheFollowingDay_OpensAgain()
    {
        // arrange
        var book = new OrderBook(Sec);
        var sequencer = new Sequencer(Day);
        sequencer.Add(book, TradingDay());
        sequencer.AdvanceTo(At(18, 0));

        // act
        var dispatched = sequencer.AdvanceTo(Day.AddDays(1).Add(OpenAt));

        // assert - one pending transition at a time, however far ahead the schedule runs
        Assert.AreEqual(2, dispatched.Count);
        Assert.IsInstanceOf<PreOpenTrading>(dispatched[0].Action);
        Assert.AreEqual(Day.AddDays(1).Add(PreOpenAt), dispatched[0].Action.Time);
        Assert.IsInstanceOf<OpenTrading>(dispatched[1].Action);
        Assert.AreEqual(Day.AddDays(1).Add(OpenAt), dispatched[1].Action.Time);
    }

    [Test]
    public void AdvanceTo_NothingDue_DispatchesNothingAndHoldsLogicalNow()
    {
        // arrange
        var sequencer = new Sequencer(Day);
        sequencer.Add(new OrderBook(Sec), TradingDay());

        // act
        var dispatched = sequencer.AdvanceTo(At(8, 0));

        // assert
        Assert.AreEqual(0, dispatched.Count);
        Assert.AreEqual(At(8, 0), sequencer.LogicalNow);
    }

    [Test]
    public void AdvanceTo_ScheduleTransitionBeatsClientFlowAtTheSameInstant()
    {
        // arrange - an order stamped exactly at the open, submitted before the open was queued at
        // all. Its counter is the lower one, so only the kind can put the venue's transition first
        var book = new OrderBook(Sec);
        var sequencer = new Sequencer(Day);
        sequencer.Add(book, TradingDay());
        sequencer.Submit(Order(Sec, "Company1", "Order1", Side.Buy, 90, At(OpenAt)));

        // act
        var dispatched = sequencer.AdvanceTo(At(OpenAt));

        // assert - the book decides what it is doing before anyone trades into it
        Assert.AreEqual(3, dispatched.Count);
        Assert.IsInstanceOf<PreOpenTrading>(dispatched[0].Action);
        Assert.IsInstanceOf<OpenTrading>(dispatched[1].Action);
        Assert.IsInstanceOf<CreateLimitOrder>(dispatched[2].Action);
    }

    [Test]
    public void AdvanceTo_InterruptionTickBeatsClientFlowAtTheSameInstant()
    {
        // arrange - an order stamped exactly at the resume deadline, submitted up front and so
        // carrying a lower counter than the tick the pause queues later. Without a kind to rank
        // them it would be dispatched into a book that should already have reopened
        var (sequencer, book) = PausedAtNoon();
        sequencer.Submit(Order(PausingSec, "Company1", "Buy2", Side.Buy, 90, Deadline));

        // act
        var dispatched = sequencer.AdvanceTo(At(12, 30));

        // assert
        Assert.AreEqual(5, dispatched.Count);

        Assert.IsInstanceOf<AdvanceTime>(dispatched[3].Action);
        var resumed = dispatched[3].Events.OfType<StatusChanged>().Single();
        Assert.AreEqual(OrderBookStatus.Open, resumed.Status);
        Assert.AreEqual(StatusChangeReason.InterruptionElapsed, resumed.Reason);

        Assert.IsInstanceOf<CreateLimitOrder>(dispatched[4].Action);
        Assert.AreEqual(OrderBookStatus.Open, book.Status);
    }

    [Test]
    public void AdvanceTo_ClientFlowAtOneInstant_DispatchedInSubmissionOrder()
    {
        // arrange - a burst sharing an instant, which the counter is what orders
        var book = new OrderBook(Sec);
        var sequencer = new Sequencer(Day);
        sequencer.Add(book, TradingDay());
        sequencer.Submit(Order(Sec, "Company1", "Order1", Side.Buy, 90, At(10, 0)));
        sequencer.Submit(Order(Sec, "Company2", "Order2", Side.Buy, 91, At(10, 0)));
        sequencer.Submit(Order(Sec, "Company1", "Order3", Side.Buy, 92, At(10, 0)));

        // act
        var dispatched = sequencer.AdvanceTo(At(10, 0));

        // assert
        Assert.AreEqual(new[] {"Order1", "Order2", "Order3"},
            dispatched.Where(d => d.Action is OrderAction)
                .Select(d => ((OrderAction) d.Action).ClientOrderId)
                .ToArray());
    }

    [Test]
    public void AdvanceTo_Pause_PokesTheBookAtItsDeadline()
    {
        // arrange - nothing else is submitted, so only the book's own event brings it back
        var (sequencer, book) = PausedAtNoon();

        // act
        var dispatched = sequencer.AdvanceTo(At(12, 30));

        // assert
        var tick = dispatched.Single(d => d.Action is AdvanceTime);
        Assert.AreEqual(Deadline, tick.Action.Time);
        Assert.AreEqual(OrderBookStatus.Open, book.Status);

        // poked punctually, the deadline and the poke are the same instant - which is what stops
        // the resume being stamped anywhere else
        var resumed = tick.Events.OfType<StatusChanged>().Single();
        Assert.AreEqual(StatusChangeReason.InterruptionElapsed, resumed.Reason);
        Assert.AreEqual(Deadline, resumed.Time);

        // and the pause resolves into one uncrossing print, stamped there too
        var matched = tick.Events.OfType<OrdersMatched>().Single();
        Assert.AreEqual(200, matched.Price);
        Assert.AreEqual(Deadline, matched.Time);
    }

    [Test]
    public void AdvanceTo_StoppingShortOfTheDeadline_LeavesTheBookPaused()
    {
        // arrange
        var (sequencer, book) = PausedAtNoon();

        // act - a minute short
        var dispatched = sequencer.AdvanceTo(Deadline - TimeSpan.FromMinutes(1));

        // assert
        Assert.AreEqual(0, dispatched.Count(d => d.Action is AdvanceTime));
        Assert.AreEqual(OrderBookStatus.Paused, book.Status);
    }

    [Test]
    public void AdvanceTo_CloseBeforeTheDeadline_LeavesTheTickInert()
    {
        // arrange - the session closes over the running pause, which clears the book's own resume
        // time. Nothing cancels the poke that was queued for it
        var (sequencer, book) = PausedAtNoon();
        sequencer.Submit(new CloseTrading {Security = PausingSec, Time = At(12, 2)});

        // act
        var dispatched = sequencer.AdvanceTo(At(12, 30));

        // assert - the tick still arrives, finds nothing to supersede, and does nothing. No
        // interruption epoch on the action, no cancellation bookkeeping
        var tick = dispatched.Single(d => d.Action is AdvanceTime);
        Assert.AreEqual(Deadline, tick.Action.Time);
        Assert.AreEqual(0, tick.Events.Count);
        Assert.AreEqual(OrderBookStatus.Closed, book.Status);
    }

    [Test]
    public void AdvanceTo_Sequence_CountsEveryDispatchWhateverQueuedIt()
    {
        // arrange
        var (sequencer, book) = PausedAtNoon();

        // act
        var dispatched = sequencer.AdvanceTo(At(23, 20));

        // assert - client flow, the schedule's own transitions and an interruption tick all count,
        // because the number is the venue's order of events rather than a count of client actions
        Assert.AreEqual(6, dispatched.Count);
        Assert.AreEqual(new long[] {1, 2, 3, 4, 5, 6}, dispatched.Select(d => d.Sequence).ToArray());
        Assert.AreEqual(1, dispatched.Count(d => d.Action is AdvanceTime), "the interruption tick");
        Assert.AreEqual(2, dispatched.Count(d => d.Action.Time >= At(23, 0)),
            "the schedule's own pre-open and open");
        Assert.AreEqual(OrderBookStatus.Open, book.Status);
    }

    [Test]
    public void AdvanceTo_SameInputs_SameDispatchOrder()
    {
        // arrange - the whole point of one queue with a total order on its entries: nothing about
        // the dispatch stream depends on anything but the inputs
        static IReadOnlyList<(long Sequence, string Action, DateTime Time)> Run()
        {
            var (sequencer, _) = PausedAtNoon();
            sequencer.Submit(Order(PausingSec, "Company1", "Buy2", Side.Buy, 90, Deadline));
            sequencer.Submit(Order(PausingSec, "Company2", "Sell2", Side.Sell, 300, At(12, 10)));

            return sequencer.AdvanceTo(At(23, 20))
                .Select(d => (d.Sequence, d.Action.GetType().Name, d.Action.Time))
                .ToList();
        }

        // act
        var first = Run();
        var second = Run();

        // assert
        Assert.AreEqual(first, second);
    }

    [Test]
    public void Add_MidSession_QueuesTheNextBoundaryAndNotTheOnesItMissed()
    {
        // arrange - registered at noon, with the session's pre-open and open already behind it
        var book = new OrderBook(Sec);
        var sequencer = new Sequencer(At(12, 0));
        sequencer.Add(book, TradingDay());

        // act
        var dispatched = sequencer.AdvanceTo(At(18, 0));

        // assert - a schedule is asked what is next, never what was missed. Bringing a book up to
        // date is the job of whoever starts the venue
        Assert.AreEqual(1, dispatched.Count);
        Assert.IsInstanceOf<CloseTrading>(dispatched[0].Action);
    }

    [Test]
    public void Add_TwiceForTheSameSecurity_ArgumentException()
    {
        // arrange
        var sequencer = new Sequencer(Day);
        sequencer.Add(new OrderBook(Sec), TradingDay());

        // assert
        Assert.Catch<ArgumentException>(
            () => sequencer.Add(new OrderBook(Sec), TradingDay())
        );
    }

    [Test]
    public void Submit_BehindLogicalNow_ArgumentException()
    {
        // arrange
        var sequencer = new Sequencer(Day);
        sequencer.Add(new OrderBook(Sec), TradingDay());
        sequencer.AdvanceTo(At(12, 0));

        // assert - the past has been dispatched and cannot be inserted into
        Assert.Catch<ArgumentException>(
            () => sequencer.Submit(Order(Sec, "Company1", "Order1", Side.Buy, 90, At(11, 59)))
        );
    }

    [Test]
    public void Submit_AtLogicalNow_Accepted()
    {
        // arrange - the boundary case on the other side: the instant itself is still open, because
        // a burst may share it
        var sequencer = new Sequencer(Day);
        sequencer.Add(new OrderBook(Sec), TradingDay());
        sequencer.AdvanceTo(At(12, 0));

        // act
        sequencer.Submit(Order(Sec, "Company1", "Order1", Side.Buy, 90, At(12, 0)));
        var dispatched = sequencer.AdvanceTo(At(12, 0));

        // assert
        Assert.AreEqual(1, dispatched.Count);
        Assert.IsInstanceOf<CreateLimitOrder>(dispatched[0].Action);
    }

    [Test]
    public void Submit_Unstamped_ArgumentException()
    {
        // arrange
        var sequencer = new Sequencer(Day);
        sequencer.Add(new OrderBook(Sec), TradingDay());

        // assert - an action with no time has no place in a queue ordered by time
        Assert.Catch<ArgumentException>(
            () => sequencer.Submit(new AdvanceTime {Security = Sec})
        );
    }

    [Test]
    public void Submit_SecurityWithNoBook_ArgumentException()
    {
        // arrange
        var sequencer = new Sequencer(Day);
        sequencer.Add(new OrderBook(Sec), TradingDay());
        var unregistered = new Security("SIZ6", SecurityType.Future, 10, 10);

        // assert - heard about where the routing mistake was made, not mid-dispatch
        Assert.Catch<ArgumentException>(
            () => sequencer.Submit(Order(unregistered, "Company1", "Order1", Side.Buy, 90, At(12, 0)))
        );
    }

    [Test]
    public void AdvanceTo_Backwards_ArgumentException()
    {
        // arrange
        var sequencer = new Sequencer(Day);
        sequencer.Add(new OrderBook(Sec), TradingDay());
        sequencer.AdvanceTo(At(12, 0));

        // assert
        Assert.Catch<ArgumentException>(() => sequencer.AdvanceTo(At(11, 0)));
    }

    [Test]
    public void AdvanceTo_HoldsLogicalNowAtTheTargetRatherThanTheLastThingDispatched()
    {
        // arrange
        var sequencer = new Sequencer(Day);
        sequencer.Add(new OrderBook(Sec), TradingDay());

        // act - the last thing dispatched was the open at 09:30
        sequencer.AdvanceTo(At(14, 0));

        // assert
        Assert.AreEqual(At(14, 0), sequencer.LogicalNow);
    }
}
