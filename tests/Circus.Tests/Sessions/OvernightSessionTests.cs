using Circus.Actions;
using Circus.Events;
using Circus.Sequencing;
using Circus.Sessions;
using NUnit.Framework;

namespace Circus.Tests.Sessions;

// A session that opens one evening and closes the next afternoon, driven end to end through a
// sequencer - the schedule producing the boundaries, the book acting on them.
//
// What is worth holding is that the trading day and the calendar disagree for the first half of
// such a session, and that everything measured in trading days follows the venue rather than the
// clock. An order resting at 23:00 on Sunday belongs to Monday's session; midnight is not a
// boundary, and nothing about it retires anything.
[TestFixture]
public class OvernightSessionTests
{
    private static readonly Instrument Gold = new("GCZ6", 10, 10);

    // Sunday evening through Monday afternoon, which is Monday's trading day throughout.
    private static readonly TradingSession Overnight =
        new(new TimeSpan(16, 45, 0), new TimeSpan(17, 0, 0), new TimeSpan(40, 0, 0), TradeDateOffset: 1);

    private static readonly DateTime Sunday = new(2000, 1, 2);
    private static readonly DateTime Monday = new(2000, 1, 3);

    private static readonly DateOnly MondayDate = DateOnly.FromDateTime(Monday);
    private static readonly DateOnly SundayDate = DateOnly.FromDateTime(Sunday);

    // 40 hours past Sunday's midnight, which is Monday afternoon.
    private static readonly DateTime Close = Sunday.Add(new TimeSpan(40, 0, 0));

    private static MarketSchedule Schedule() => new(new[] {Overnight});

    // Registered in the maintenance window ahead of the session, so the sequencer's first
    // boundary is that session's own pre-open rather than something it has already missed.
    private static (Sequencer Sequencer, OrderBook Book) Venue()
    {
        var book = new OrderBook(Gold);
        var sequencer = new Sequencer(Sunday.Add(new TimeSpan(16, 30, 0)));
        sequencer.Add(book, Schedule());
        return (sequencer, book);
    }

    private static CreateLimitOrder Order(string clientOrderId, OrderValidity validity, DateTime time) =>
        new()
        {
            Symbol = Gold.Symbol, Time = time, CompanyId = "Company1", ClientOrderId = clientOrderId,
            OrderValidity = validity, Side = Side.Buy, Quantity = 5, Price = 100
        };

    private static IReadOnlyList<OrderBookEvent> EventsOf(IReadOnlyList<Dispatched> dispatched) =>
        dispatched.SelectMany(d => d.Events).ToList();

    [Test]
    public void AdvanceTo_DrivesTheBookAcrossMidnight()
    {
        // arrange
        var (sequencer, book) = Venue();

        // act - to the middle of the night, hours past the date change
        var dispatched = sequencer.AdvanceTo(Monday.Add(new TimeSpan(3, 0, 0)));

        // assert - the evening's two boundaries and nothing else. Midnight is not a boundary
        Assert.AreEqual(2, dispatched.Count);
        Assert.IsInstanceOf<PreOpenTrading>(dispatched[0].Action);
        Assert.IsInstanceOf<OpenTrading>(dispatched[1].Action);
        Assert.AreEqual(OrderBookStatus.Open, book.Status);
    }

    [Test]
    public void AdvanceTo_TheSessionsClose_IsOnTheFollowingAfternoon()
    {
        // arrange
        var (sequencer, book) = Venue();

        // act
        var dispatched = sequencer.AdvanceTo(Close);

        // assert - a boundary 40 hours past its anchor, landing on the day after the one the
        // session opened on
        var close = dispatched.Select(d => d.Action).OfType<CloseTrading>().Single();
        Assert.AreEqual(Monday.Add(new TimeSpan(16, 0, 0)), close.Time);
        Assert.IsTrue(close.EndsTradingDay);
        Assert.AreEqual(OrderBookStatus.Closed, book.Status);
    }

    [Test]
    public void SessionActions_CarryTheTradingDayRatherThanTheDate()
    {
        // arrange
        var (sequencer, _) = Venue();

        // act
        var dispatched = sequencer.AdvanceTo(Close);

        // assert - the pre-open and open fall on Sunday and are Monday's business; the close
        // falls on Monday and is the same day's, so the trade date never moves within a session
        var session = dispatched.Select(d => d.Action).OfType<SessionAction>().ToList();
        Assert.AreEqual(3, session.Count);
        CollectionAssert.AreEqual(new DateOnly?[] {MondayDate, MondayDate, MondayDate},
            session.Select(a => a.TradeDate).ToList());
        Assert.AreEqual(Sunday, session[0].Time.Date);
        Assert.AreEqual(Monday, session[2].Time.Date);
    }

    [Test]
    public void DayOrder_RestingOverMidnight_ExpiresAtTheSessionsClose()
    {
        // arrange - a day order sent in the evening, which under a calendar-day reading would be
        // yesterday's by the time the session ends
        var (sequencer, _) = Venue();
        sequencer.Submit(Order("Day1", new OrderValidity.Day(), Sunday.Add(new TimeSpan(23, 0, 0))));

        // act - to just before the close, then over it
        var overnight = sequencer.AdvanceTo(Monday.Add(new TimeSpan(15, 59, 0)));
        var atTheClose = sequencer.AdvanceTo(Close);

        // assert
        Assert.IsEmpty(EventsOf(overnight).OfType<ExpireOrderConfirmed>(),
            "midnight retires nothing - the session it was sent into is still running");

        var expired = EventsOf(atTheClose).OfType<ExpireOrderConfirmed>().Single();
        Assert.AreEqual("Day1", expired.Order.ClientOrderId);
        Assert.AreEqual(Close, expired.Time);
    }

    [Test]
    public void GoodTilDate_ForTheTradingDay_ExpiresAtThatSessionsClose()
    {
        // arrange - good till Monday, sent on Sunday evening into Monday's session
        var (sequencer, _) = Venue();
        sequencer.Submit(Order("GTD1", new OrderValidity.GoodTilDate {Date = MondayDate},
            Sunday.Add(new TimeSpan(23, 0, 0))));

        // act
        var dispatched = sequencer.AdvanceTo(Close);

        // assert - the close is the end of the day the order was good till, so it goes there.
        // Dating the book from the clock would have retired it at this same close for the wrong
        // reason, having read the whole evening as Sunday
        var expired = EventsOf(dispatched).OfType<ExpireOrderConfirmed>().Single();
        Assert.AreEqual("GTD1", expired.Order.ClientOrderId);
        Assert.AreEqual(Close, expired.Time);
    }

    [Test]
    public void GoodTilDate_ForTheDayTheSessionOpenedOn_IsRejected()
    {
        // arrange - good till Sunday, sent at 23:00 on Sunday. By the clock that is today and the
        // order has hours left; by the trading day the venue is already on Monday and Sunday is
        // behind it, so this is a date in the past
        var (sequencer, _) = Venue();
        sequencer.Submit(Order("Stale", new OrderValidity.GoodTilDate {Date = SundayDate},
            Sunday.Add(new TimeSpan(23, 0, 0))));

        // act
        var dispatched = sequencer.AdvanceTo(Close);

        // assert
        var rejected = EventsOf(dispatched).OfType<CreateOrderRejected>().Single();
        Assert.AreEqual("Stale", rejected.ClientOrderId);
        Assert.AreEqual(OrderRejectedReason.InvalidExpireDate, rejected.Reason);
    }

    [Test]
    public void ExchangeOrderIds_AreSeededFromTheTradingDay()
    {
        // arrange
        var (sequencer, _) = Venue();
        sequencer.Submit(Order("Evening", new OrderValidity.Day(), Sunday.Add(new TimeSpan(23, 0, 0))));

        // act
        var dispatched = sequencer.AdvanceTo(Close);

        // assert - Monday's run of ids, issued on Sunday evening. A seed taken from the clock
        // would start the session on Sunday's run and step up to Monday's at midnight, halfway
        // through a session whose ids are supposed to name one day
        var created = EventsOf(dispatched).OfType<CreateOrderConfirmed>().Single();
        var seed = ((MondayDate.Year * 10000) + (MondayDate.Month * 100) + MondayDate.Day) * 10000000000L;
        var id = long.Parse(created.Order.ExchangeOrderId);

        Assert.GreaterOrEqual(id, seed);
        Assert.Less(id, seed + 10000000000L);
    }

    [Test]
    public void ADayDrivenByHand_StillDatesItselfFromTheClock()
    {
        // arrange - no schedule involved, so nothing tells the book which day it is trading
        var book = new OrderBook(Gold);

        // act
        book.PreOpenTrading(time: Monday.Add(new TimeSpan(9, 0, 0)));
        book.OpenTrading(time: Monday.Add(new TimeSpan(9, 30, 0)));
        book.CreateLimitOrder("Company1", "Day1", new OrderValidity.Day(), Side.Buy, 5, 100,
            time: Monday.Add(new TimeSpan(10, 0, 0)));
        var closed = book.CloseTrading(time: Monday.Add(new TimeSpan(17, 0, 0)));

        // assert - which is what every schedule that stays within its day means anyway
        Assert.AreEqual("Day1", closed.OfType<ExpireOrderConfirmed>().Single().Order.ClientOrderId);
    }
}
