using Circus.Sessions;
using NUnit.Framework;

namespace Circus.Tests.Sessions;

// The schedule as a question rather than a walk: given an instant, what is due next. Nothing here
// holds state, so the same question asked twice - or asked out of order - answers the same way,
// which is the property a stateful walker cannot offer and a queue in front of many books needs.
[TestFixture]
public class MarketScheduleTests
{
    private static readonly TimeSpan PreOpen = new(1, 0, 0);
    private static readonly TimeSpan Open = new(1, 10, 0);
    private static readonly TimeSpan Close = new(22, 10, 0);

    private static readonly DateTime Day = new(2000, 1, 1);
    private static readonly DateTime NextDay = new(2000, 1, 2);

    // A day with a morning and an afternoon session, closing for a break in between.
    private static readonly TradingSession Morning =
        new(new TimeSpan(8, 0, 0), new TimeSpan(8, 30, 0), new TimeSpan(11, 0, 0));

    private static readonly TradingSession Afternoon =
        new(new TimeSpan(13, 0, 0), new TimeSpan(13, 30, 0), new TimeSpan(16, 0, 0));

    private static MarketSchedule OneSession() => new(PreOpen, Open, Close);

    private static MarketSchedule TwoSessions() => new(new[] {Morning, Afternoon});

    // A repeating day always has something next, so every case here expects a value.
    private static ScheduledTransition NextAfter(MarketSchedule schedule, DateTime time)
    {
        var transition = schedule.NextAfter(time);
        Assert.IsNotNull(transition, "a day repeated indefinitely never runs out of boundaries");
        return transition.Value;
    }

    [Test]
    public void NextAfter_BeforeTheFirstPreOpen_PreOpensToday()
    {
        // act
        var next = NextAfter(OneSession(), Day);

        // assert
        Assert.AreEqual(OrderBookStatus.PreOpen, next.Status);
        Assert.AreEqual(Day.Add(PreOpen), next.Time);
    }

    [Test]
    public void NextAfter_OnThePreOpen_Opens()
    {
        // act - strictly after, so standing on a boundary asks for the one that follows
        var next = NextAfter(OneSession(), Day.Add(PreOpen));

        // assert
        Assert.AreEqual(OrderBookStatus.Open, next.Status);
        Assert.AreEqual(Day.Add(Open), next.Time);
    }

    [Test]
    public void NextAfter_DuringThePreOpen_Opens()
    {
        // act
        var next = NextAfter(OneSession(), Day.Add(new TimeSpan(1, 5, 0)));

        // assert
        Assert.AreEqual(OrderBookStatus.Open, next.Status);
        Assert.AreEqual(Day.Add(Open), next.Time);
    }

    [Test]
    public void NextAfter_OnTheOpen_Closes()
    {
        // act
        var next = NextAfter(OneSession(), Day.Add(Open));

        // assert
        Assert.AreEqual(OrderBookStatus.Closed, next.Status);
        Assert.AreEqual(Day.Add(Close), next.Time);
        Assert.IsTrue(next.EndsTradingDay, "a lone session is also the day's last");
    }

    [Test]
    public void NextAfter_DuringTheSession_Closes()
    {
        // act
        var next = NextAfter(OneSession(), Day.Add(new TimeSpan(12, 0, 0)));

        // assert
        Assert.AreEqual(OrderBookStatus.Closed, next.Status);
        Assert.AreEqual(Day.Add(Close), next.Time);
    }

    [Test]
    public void NextAfter_OnTheClose_PreOpensTomorrow()
    {
        // act
        var next = NextAfter(OneSession(), Day.Add(Close));

        // assert
        Assert.AreEqual(OrderBookStatus.PreOpen, next.Status);
        Assert.AreEqual(NextDay.Add(PreOpen), next.Time);
    }

    [Test]
    public void NextAfter_AfterTheClose_PreOpensTomorrow()
    {
        // act
        var next = NextAfter(OneSession(), Day.Add(new TimeSpan(23, 0, 0)));

        // assert
        Assert.AreEqual(OrderBookStatus.PreOpen, next.Status);
        Assert.AreEqual(NextDay.Add(PreOpen), next.Time);
    }

    [Test]
    public void NextAfter_DaysLater_AnchorsOnThatDay()
    {
        // act - nothing was asked about the days in between, and nothing needs to be
        var later = new DateTime(2000, 3, 15);
        var next = NextAfter(OneSession(), later);

        // assert
        Assert.AreEqual(OrderBookStatus.PreOpen, next.Status);
        Assert.AreEqual(later.Add(PreOpen), next.Time);
    }

    [Test]
    public void NextAfter_IntradayClose_DoesNotEndTradingDay()
    {
        // act
        var next = NextAfter(TwoSessions(), Day.Add(new TimeSpan(10, 0, 0)));

        // assert - the afternoon is still to come, so day orders must survive this close
        Assert.AreEqual(OrderBookStatus.Closed, next.Status);
        Assert.AreEqual(Day.Add(Morning.Close), next.Time);
        Assert.IsFalse(next.EndsTradingDay);
    }

    [Test]
    public void NextAfter_OnTheIntradayClose_PreOpensTheAfternoon()
    {
        // act
        var next = NextAfter(TwoSessions(), Day.Add(Morning.Close));

        // assert
        Assert.AreEqual(OrderBookStatus.PreOpen, next.Status);
        Assert.AreEqual(Day.Add(Afternoon.PreOpen), next.Time);
    }

    [Test]
    public void NextAfter_DuringTheBreak_PreOpensTheAfternoon()
    {
        // act
        var next = NextAfter(TwoSessions(), Day.Add(new TimeSpan(12, 0, 0)));

        // assert
        Assert.AreEqual(OrderBookStatus.PreOpen, next.Status);
        Assert.AreEqual(Day.Add(Afternoon.PreOpen), next.Time);
    }

    [Test]
    public void NextAfter_FinalClose_EndsTradingDay()
    {
        // act
        var next = NextAfter(TwoSessions(), Day.Add(new TimeSpan(15, 0, 0)));

        // assert
        Assert.AreEqual(OrderBookStatus.Closed, next.Status);
        Assert.AreEqual(Day.Add(Afternoon.Close), next.Time);
        Assert.IsTrue(next.EndsTradingDay);
    }

    [Test]
    public void NextAfter_AfterTheFinalClose_WrapsToTomorrowsFirstSession()
    {
        // act
        var next = NextAfter(TwoSessions(), Day.Add(Afternoon.Close));

        // assert
        Assert.AreEqual(OrderBookStatus.PreOpen, next.Status);
        Assert.AreEqual(NextDay.Add(Morning.PreOpen), next.Time);
    }

    [Test]
    public void NextAfter_WalkedForward_VisitsEveryBoundaryInOrder()
    {
        // arrange - feeding each answer back in is how a queue would drive this
        var schedule = TwoSessions();
        var walk = new List<ScheduledTransition>();
        var time = Day;

        // act
        for (var i = 0; i < 7; i++)
        {
            var next = NextAfter(schedule, time);
            walk.Add(next);
            time = next.Time;
        }

        // assert - the whole day in order, then the roll into the next one
        Assert.AreEqual(
            new[]
            {
                new ScheduledTransition(Day.Add(Morning.PreOpen), OrderBookStatus.PreOpen),
                new ScheduledTransition(Day.Add(Morning.Open), OrderBookStatus.Open),
                new ScheduledTransition(Day.Add(Morning.Close), OrderBookStatus.Closed, false),
                new ScheduledTransition(Day.Add(Afternoon.PreOpen), OrderBookStatus.PreOpen),
                new ScheduledTransition(Day.Add(Afternoon.Open), OrderBookStatus.Open),
                new ScheduledTransition(Day.Add(Afternoon.Close), OrderBookStatus.Closed),
                new ScheduledTransition(NextDay.Add(Morning.PreOpen), OrderBookStatus.PreOpen)
            },
            walk);
    }

    [Test]
    public void NextAfter_AskedOutOfOrder_AnswersTheSame()
    {
        // arrange - the point of being stateless: a caller may ask about any instant, in any
        // order, and a replay asking about yesterday gets yesterday's answer
        var schedule = TwoSessions();
        var duringTheAfternoon = Day.Add(new TimeSpan(15, 0, 0));

        // act
        var first = NextAfter(schedule, duringTheAfternoon);
        NextAfter(schedule, Day.Add(new TimeSpan(9, 0, 0)));
        var again = NextAfter(schedule, duringTheAfternoon);

        // assert
        Assert.AreEqual(first, again);
    }

    [Test]
    public void NextAfter_CloseTouchingTheNextPreOpen_StepsOverThePreOpen()
    {
        // arrange - a session beginning the moment the previous one closes puts two boundaries on
        // one instant, and a query keyed on time alone cannot return both
        var touching = new TradingSession(new TimeSpan(11, 0, 0), new TimeSpan(11, 30, 0),
            new TimeSpan(16, 0, 0));
        var schedule = new MarketSchedule(new[] {Morning, touching});

        // act
        var close = NextAfter(schedule, Day.Add(new TimeSpan(10, 0, 0)));
        var afterTheClose = NextAfter(schedule, close.Time);

        // assert - the close is reached, and asking on from it lands on the open rather than the
        // pre-open sharing that instant. A limitation of asking by time, pinned here so whatever
        // consumes this next has to decide about it rather than discover it
        Assert.AreEqual(OrderBookStatus.Closed, close.Status);
        Assert.AreEqual(Day.Add(Morning.Close), close.Time);
        Assert.AreEqual(OrderBookStatus.Open, afterTheClose.Status);
        Assert.AreEqual(Day.Add(touching.Open), afterTheClose.Time);
    }

    [Test]
    public void Constructor_NoSessions_ArgumentException()
    {
        // assert
        Assert.Catch<ArgumentException>(
            () => new MarketSchedule(Array.Empty<TradingSession>())
        );
    }

    [Test]
    public void Constructor_OpenBeforePreOpen_ArgumentException()
    {
        // assert
        Assert.Catch<ArgumentException>(
            () => new MarketSchedule(new TimeSpan(1, 20, 0), new TimeSpan(1, 10, 0), Close)
        );
    }

    [Test]
    public void Constructor_CloseBeforeOpen_ArgumentException()
    {
        // assert
        Assert.Catch<ArgumentException>(
            () => new MarketSchedule(PreOpen, Open, new TimeSpan(1, 5, 0))
        );
    }

    [Test]
    public void Constructor_UnorderedSessions_ArgumentException()
    {
        // assert
        Assert.Catch<ArgumentException>(
            () => new MarketSchedule(new[] {Afternoon, Morning})
        );
    }

    [Test]
    public void Constructor_OverlappingSessions_ArgumentException()
    {
        // arrange - the afternoon pre-opens before the morning has closed
        var overlapping = new TradingSession(new TimeSpan(10, 0, 0), new TimeSpan(13, 30, 0),
            new TimeSpan(16, 0, 0));

        // assert
        Assert.Catch<ArgumentException>(
            () => new MarketSchedule(new[] {Morning, overlapping})
        );
    }

    [Test]
    public void Constructor_TouchingSessions_Success()
    {
        // arrange - a session may begin the moment the previous one closes
        var touching = new TradingSession(new TimeSpan(11, 0, 0), new TimeSpan(11, 30, 0),
            new TimeSpan(16, 0, 0));

        // assert
        new MarketSchedule(new[] {Morning, touching});
    }
}
