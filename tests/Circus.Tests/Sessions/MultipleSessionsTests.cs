using Circus.Actions;
using Circus.Events;
using Circus.Tests.Helpers;
using Circus.Time;
using NUnit.Framework;

namespace Circus.Tests.Sessions;

// A trading day can hold more than one session. Sequence numbers are seeded from the date, so
// the thing to prove is that a second session on the same date carries on issuing ids rather
// than restarting - orders surviving the break still hold the ids a restart would hand out
// again, and _orders is keyed on exactly those.
[TestFixture]
public class MultipleSessionsTests
{
    private static readonly Instrument Gold = new("GCZ6", 10, 10);

    private static readonly DateTime MorningSession = new(2000, 1, 1, 9, 0, 0);
    private static readonly DateTime AfternoonSession = new(2000, 1, 1, 14, 0, 0);
    private static readonly DateTime NextDay = new(2000, 1, 2, 9, 0, 0);

    private static readonly string CompanyId1 = "Company1";
    private static readonly string CompanyId2 = "Company2";

    private static readonly string OrderId1 = "Order1";
    private static readonly string OrderId2 = "Order2";
    private static readonly string OrderId3 = "Order3";
    private static readonly string OrderId4 = "Order4";

    private ManualClock Clock;
    private IOrderBook Book;

    [SetUp]
    public void SetUp()
    {
        Clock = new ManualClock(MorningSession);
        Book = new TimestampingOrderBook(Gold, Clock);
    }

    // The seed OrderBook derives from a date, so a test can say which run of ids it
    // expects without hard-coding an 18-digit literal.
    private static long SeedFor(DateTime date) =>
        ((date.Year * 10000) + (date.Month * 100) + date.Day) * 10000000000L;

    private static long ExchangeOrderIdOf(IOrderBook book, string companyId, string clientOrderId,
        OrderValidity validity, Side side, int quantity, decimal price)
    {
        var events = book.CreateLimitOrder(companyId, clientOrderId, validity, side, quantity, price);
        var created = events.OfType<CreateOrderConfirmed>().Single();
        return long.Parse(created.Order.ExchangeOrderId);
    }

    [Test]
    public void TwoSessionsSameDay_SurvivingGoodTilCanceledOrder_NoIdCollision()
    {
        // arrange - a GTC order rests through the morning session and survives the break, so it
        // still holds its id when the afternoon session starts
        Book.UpdateStatus(OrderBookStatus.PreOpen);
        Book.UpdateStatus(OrderBookStatus.Open);
        var morningId = ExchangeOrderIdOf(Book, CompanyId1, OrderId1, new OrderValidity.GoodTilCanceled(),
            Side.Buy, 5, 100);
        Book.CloseTrading(endsTradingDay: false);

        // act - the same date, so the seed is one the counter has already passed
        Clock.SetCurrentTime(AfternoonSession);
        Book.UpdateStatus(OrderBookStatus.PreOpen);
        Book.UpdateStatus(OrderBookStatus.Open);
        var afternoonId = ExchangeOrderIdOf(Book, CompanyId2, OrderId2, new OrderValidity.GoodTilCanceled(),
            Side.Buy, 5, 90);

        // assert
        Assert.IsTrue(afternoonId > morningId, "the afternoon session continues the morning's run of ids");
        Assert.AreEqual(SeedFor(MorningSession) + 1, morningId);
        Assert.AreEqual(SeedFor(MorningSession) + 2, afternoonId);
    }

    [Test]
    public void TwoSessionsSameDay_CompletedOrderIdsNotReissued()
    {
        // arrange - both morning orders fill completely, so they leave the working book and are
        // held as completed under their ids
        Book.UpdateStatus(OrderBookStatus.PreOpen);
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 5, 100);
        Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Sell, 5, 100);
        Book.CloseTrading(endsTradingDay: false);

        // act
        Clock.SetCurrentTime(AfternoonSession);
        Book.UpdateStatus(OrderBookStatus.PreOpen);
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId3, new OrderValidity.Day(), Side.Buy, 5, 100);
        var events = Book.CreateLimitOrder(CompanyId2, OrderId4, new OrderValidity.Day(), Side.Sell, 5, 100);

        // assert - the afternoon pair traded rather than colliding with the morning pair's ids
        var matched = events.Trades().Single();
        Assert.AreEqual(100, matched.Price);
        Assert.AreEqual(5, matched.Quantity);
    }

    [Test]
    public void PreOpenTwice_SameDay_DoesNotRestartSequenceNumbers()
    {
        // arrange - re-entering pre-open without an intervening close (as reopening a volatility
        // pause does) must not look like the start of a fresh run of ids either
        Book.UpdateStatus(OrderBookStatus.PreOpen);
        var first = ExchangeOrderIdOf(Book, CompanyId1, OrderId1, new OrderValidity.GoodTilCanceled(),
            Side.Buy, 5, 100);

        // act
        Book.UpdateStatus(OrderBookStatus.PreOpen);
        var second = ExchangeOrderIdOf(Book, CompanyId2, OrderId2, new OrderValidity.GoodTilCanceled(),
            Side.Buy, 5, 90);

        // assert
        Assert.IsTrue(second > first);
    }

    [Test]
    public void NewDay_SeedsFromDate()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.PreOpen);
        Book.UpdateStatus(OrderBookStatus.Open);
        var day1Id = ExchangeOrderIdOf(Book, CompanyId1, OrderId1, new OrderValidity.GoodTilCanceled(),
            Side.Buy, 5, 100);
        Book.CloseTrading();

        // act
        Clock.SetCurrentTime(NextDay);
        Book.UpdateStatus(OrderBookStatus.PreOpen);
        Book.UpdateStatus(OrderBookStatus.Open);
        var day2Id = ExchangeOrderIdOf(Book, CompanyId2, OrderId2, new OrderValidity.GoodTilCanceled(),
            Side.Buy, 5, 90);

        // assert - a new date still re-anchors, so an id carries the day it was issued
        Assert.AreEqual(SeedFor(MorningSession) + 1, day1Id);
        Assert.AreEqual(SeedFor(NextDay) + 1, day2Id);
    }

    [Test]
    public void IntradayClose_DayOrderSurvives()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 5, 100);

        // act - closing for a break, with a session still to come today
        var events = Book.CloseTrading(endsTradingDay: false);

        // assert
        Assert.AreEqual(1, events.OrderFlow().Count);
        Assert.IsInstanceOf<StatusChanged>(events[0]);
        Assert.AreEqual(0, events.OfType<ExpireOrderConfirmed>().Count(), "a break does not retire day orders");
    }

    [Test]
    public void IntradayClose_DayOrderStillTradesInTheNextSession()
    {
        // arrange - the surviving order has to be genuinely resting, not merely unexpired
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 5, 100);
        Book.CloseTrading(endsTradingDay: false);

        Clock.SetCurrentTime(AfternoonSession);
        Book.UpdateStatus(OrderBookStatus.PreOpen);
        Book.UpdateStatus(OrderBookStatus.Open);

        // act
        var events = Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Sell, 5, 100);

        // assert
        var matched = events.Trades().Single();
        Assert.AreEqual(100, matched.Price);
        Assert.AreEqual(5, matched.Quantity);
    }

    [Test]
    public void FinalClose_DayOrderExpires()
    {
        // arrange - survives the break, then meets the close that ends the day
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 5, 100);
        Book.CloseTrading(endsTradingDay: false);

        Clock.SetCurrentTime(AfternoonSession);
        Book.UpdateStatus(OrderBookStatus.PreOpen);
        Book.UpdateStatus(OrderBookStatus.Open);

        // act
        var events = Book.CloseTrading();

        // assert
        var expired = events.OfType<ExpireOrderConfirmed>().Single();
        Assert.AreEqual(AfternoonSession, expired.Time);
        Assert.AreEqual(CompanyId1, expired.CompanyId);
        Assert.AreEqual(OrderId1, expired.Order.ClientOrderId);
        Assert.AreEqual(OrderStatus.Expired, expired.Order.Status);
    }

    [Test]
    public void IntradayClose_GoodTilDateOrderDueToday_SurvivesUntilFinalClose()
    {
        // arrange - good til today, so the day's last close is what retires it
        Book.UpdateStatus(OrderBookStatus.Open);
        var goodTilDate = DateOnly.FromDateTime(MorningSession);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.GoodTilDate {Date = goodTilDate},
            Side.Buy, 5, 100);

        // act
        var breakEvents = Book.CloseTrading(endsTradingDay: false);

        Clock.SetCurrentTime(AfternoonSession);
        Book.UpdateStatus(OrderBookStatus.PreOpen);
        Book.UpdateStatus(OrderBookStatus.Open);
        var closeEvents = Book.CloseTrading();

        // assert
        Assert.AreEqual(0, breakEvents.OfType<ExpireOrderConfirmed>().Count(),
            "not due until the day actually ends");

        var expired = closeEvents.OfType<ExpireOrderConfirmed>().Single();
        Assert.AreEqual(OrderId1, expired.Order.ClientOrderId);
        Assert.AreEqual(OrderStatus.Expired, expired.Order.Status);
    }

    [Test]
    public void GoodTilCanceledOrder_SurvivesFinalCloseToo()
    {
        // arrange - the day ending retires day orders, never a GTC one
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.GoodTilCanceled(), Side.Buy, 5, 100);

        // act
        var events = Book.CloseTrading();

        // assert
        Assert.AreEqual(1, events.OrderFlow().Count);
        Assert.IsInstanceOf<StatusChanged>(events[0]);
    }
}
