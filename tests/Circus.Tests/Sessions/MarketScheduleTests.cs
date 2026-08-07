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
        var today = DateOnly.FromDateTime(Day);
        var tomorrow = DateOnly.FromDateTime(NextDay);

        Assert.AreEqual(
            new[]
            {
                new ScheduledTransition(Day.Add(Morning.PreOpen), OrderBookStatus.PreOpen, today),
                new ScheduledTransition(Day.Add(Morning.Open), OrderBookStatus.Open, today),
                new ScheduledTransition(Day.Add(Morning.Close), OrderBookStatus.Closed, today, false),
                new ScheduledTransition(Day.Add(Afternoon.PreOpen), OrderBookStatus.PreOpen, today),
                new ScheduledTransition(Day.Add(Afternoon.Open), OrderBookStatus.Open, today),
                new ScheduledTransition(Day.Add(Afternoon.Close), OrderBookStatus.Closed, today),
                new ScheduledTransition(NextDay.Add(Morning.PreOpen), OrderBookStatus.PreOpen, tomorrow)
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
    public void Constructor_TouchingSessions_ArgumentException()
    {
        // arrange - a session beginning the moment the previous one closes puts two boundaries on
        // one instant, and a query keyed on time alone cannot return both: the close would be
        // answered and asking on from it would step over the pre-open, opening a session that
        // never pre-opened
        var touching = new TradingSession(new TimeSpan(11, 0, 0), new TimeSpan(11, 30, 0),
            new TimeSpan(16, 0, 0));

        // assert
        Assert.Catch<ArgumentException>(
            () => new MarketSchedule(new[] {Morning, touching})
        );
    }

    [Test]
    public void Constructor_OpenOnThePreOpen_ArgumentException()
    {
        // assert - the same instant shared by two of one session's own boundaries, and skipped
        // for the same reason
        Assert.Catch<ArgumentException>(
            () => new MarketSchedule(PreOpen, PreOpen, Close)
        );
    }

    [Test]
    public void Constructor_CloseOnTheOpen_ArgumentException()
    {
        // assert
        Assert.Catch<ArgumentException>(
            () => new MarketSchedule(PreOpen, Open, Open)
        );
    }

    [Test]
    public void Constructor_DaySpanningTwentyFourHours_ArgumentException()
    {
        // assert - the day's last close landing on tomorrow's first pre-open, which is the wrap
        // around version of two sessions touching
        Assert.Catch<ArgumentException>(
            () => new MarketSchedule(new TimeSpan(17, 0, 0), new TimeSpan(17, 30, 0), new TimeSpan(41, 0, 0))
        );
    }

    [Test]
    public void Constructor_FirstPreOpenPastItsOwnDay_ArgumentException()
    {
        // assert - an anchor that names no particular day
        Assert.Catch<ArgumentException>(
            () => new MarketSchedule(new TimeSpan(25, 0, 0), new TimeSpan(25, 30, 0), new TimeSpan(30, 0, 0))
        );
    }

    // Globex's shape: pre-open in the late afternoon, open an evening, close the following
    // afternoon. The whole session is one trading day and it is the day it closes on, so its
    // evening half is dated a day ahead of the clock.
    private static readonly TradingSession Overnight =
        new(new TimeSpan(16, 45, 0), new TimeSpan(17, 0, 0), new TimeSpan(40, 0, 0), TradeDateOffset: 1);

    private static MarketSchedule OvernightSession() => new(new[] {Overnight});

    [Test]
    public void NextAfter_DuringTheMorning_ClosesThisAfternoon()
    {
        // act - a venue this shape is in a session for all but 45 minutes of the day, and the
        // session it is in at breakfast is the one that opened last night
        var next = NextAfter(OvernightSession(), Day.Add(new TimeSpan(9, 0, 0)));

        // assert
        Assert.AreEqual(OrderBookStatus.Closed, next.Status);
        Assert.AreEqual(Day.Add(new TimeSpan(16, 0, 0)), next.Time);
        Assert.AreEqual(DateOnly.FromDateTime(Day), next.TradeDate,
            "last night's session trades for today");
    }

    [Test]
    public void NextAfter_DuringTheEvening_ClosesTomorrowAfternoon()
    {
        // act
        var next = NextAfter(OvernightSession(), Day.Add(new TimeSpan(20, 0, 0)));

        // assert - the close is on the next calendar day, which no same-day schedule can say
        Assert.AreEqual(OrderBookStatus.Closed, next.Status);
        Assert.AreEqual(NextDay.Add(new TimeSpan(16, 0, 0)), next.Time);
        Assert.IsTrue(next.EndsTradingDay);
    }

    [Test]
    public void NextAfter_AfterMidnight_StillInLastNightsSession()
    {
        // act - the case a schedule anchored on the asking date cannot answer: nothing opened
        // today, and what is running began yesterday
        var next = NextAfter(OvernightSession(), NextDay.Add(new TimeSpan(2, 0, 0)));

        // assert
        Assert.AreEqual(OrderBookStatus.Closed, next.Status);
        Assert.AreEqual(NextDay.Add(new TimeSpan(16, 0, 0)), next.Time);
        Assert.AreEqual(DateOnly.FromDateTime(NextDay), next.TradeDate,
            "past midnight the clock has caught up with the trading day, which never moved");
    }

    [Test]
    public void NextAfter_BetweenTheCloseAndTheNextPreOpen_PreOpensTonight()
    {
        // act - the maintenance window, the only part of the day this venue is not in a session
        var next = NextAfter(OvernightSession(), NextDay.Add(new TimeSpan(16, 30, 0)));

        // assert
        Assert.AreEqual(OrderBookStatus.PreOpen, next.Status);
        Assert.AreEqual(NextDay.Add(Overnight.PreOpen), next.Time);
    }

    [Test]
    public void NextAfter_OvernightWalkedForward_VisitsEveryBoundaryInOrder()
    {
        // arrange
        var schedule = OvernightSession();
        var walk = new List<ScheduledTransition>();

        // From inside the maintenance window, the only instant of the day this venue is between
        // sessions rather than in one.
        var time = Day.Add(new TimeSpan(16, 30, 0));

        // act
        for (var i = 0; i < 4; i++)
        {
            var next = NextAfter(schedule, time);
            walk.Add(next);
            time = next.Time;
        }

        // assert - one session per calendar day, each dated a day ahead of the day it opens on,
        // and the close of one landing between the pre-open and open of nothing at all
        var dayAfter = NextDay.AddDays(1);
        Assert.AreEqual(
            new[]
            {
                new ScheduledTransition(Day.Add(Overnight.PreOpen), OrderBookStatus.PreOpen,
                    DateOnly.FromDateTime(NextDay)),
                new ScheduledTransition(Day.Add(Overnight.Open), OrderBookStatus.Open,
                    DateOnly.FromDateTime(NextDay)),
                new ScheduledTransition(NextDay.Add(new TimeSpan(16, 0, 0)), OrderBookStatus.Closed,
                    DateOnly.FromDateTime(NextDay)),
                new ScheduledTransition(NextDay.Add(Overnight.PreOpen), OrderBookStatus.PreOpen,
                    DateOnly.FromDateTime(dayAfter))
            },
            walk);
    }

    [Test]
    public void NextAfter_DaySessionThenAnEveningOne_DatesEachToItsOwnTradingDay()
    {
        // arrange - a cash session trading for today, and an evening one that is tomorrow's
        // business already, which is what makes the trade date a property of the session rather
        // than of the calendar
        var cash = new TradingSession(new TimeSpan(8, 0, 0), new TimeSpan(8, 30, 0), new TimeSpan(16, 0, 0));
        var evening = new TradingSession(new TimeSpan(17, 0, 0), new TimeSpan(17, 30, 0),
            new TimeSpan(21, 0, 0), TradeDateOffset: 1);
        var schedule = new MarketSchedule(new[] {cash, evening});

        // act
        var cashClose = NextAfter(schedule, Day.Add(new TimeSpan(12, 0, 0)));
        var eveningClose = NextAfter(schedule, Day.Add(new TimeSpan(18, 0, 0)));

        // assert - and only the evening close ends a trading day, the cash close being a session
        // with another still to come
        Assert.AreEqual(DateOnly.FromDateTime(Day), cashClose.TradeDate);
        Assert.IsFalse(cashClose.EndsTradingDay);
        Assert.AreEqual(DateOnly.FromDateTime(NextDay), eveningClose.TradeDate);
        Assert.IsTrue(eveningClose.EndsTradingDay);
    }
}
