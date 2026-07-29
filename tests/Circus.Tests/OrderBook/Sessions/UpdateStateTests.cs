using Circus.OrderBook;
using Circus.OrderBook.Actions;
using Circus.OrderBook.Events;
using Circus.Time;
using NUnit.Framework;

namespace Circus.Tests.OrderBook.Sessions;

[TestFixture]
public class UpdateStateTests
{
    private static readonly Security Sec = new("GCZ6", SecurityType.Future, 10, 10);

    private static readonly DateTime Now1 = new(2000, 1, 1, 12, 0, 0);
    private static readonly DateTime Now2 = new(2000, 1, 1, 12, 1, 0);
    private static readonly DateTime Now3 = new(2000, 1, 1, 12, 2, 0);

    private static readonly string CompanyId1 = "Company1";
    private static readonly string CompanyId2 = "Company2";
    private static readonly string CompanyId3 = "Company3";

    private static readonly string OrderId1 = "Order1";
    private static readonly string OrderId2 = "Order2";
    private static readonly string OrderId3 = "Order3";

    private static ManualClock Clock;
    private static IOrderBook Book;

    [SetUp]
    public void SetUp()
    {
        Clock = new ManualClock(Now1);
        Book = new InMemoryOrderBook(Sec, Clock);
    }

    [TestCase(OrderBookStatus.PreOpen)]
    [TestCase(OrderBookStatus.Open)]
    [TestCase(OrderBookStatus.Closed)]
    [TestCase(OrderBookStatus.Paused)]
    [TestCase(OrderBookStatus.Halted)]
    public void Valid_Success(OrderBookStatus status)
    {
        // act
        var events = Book.UpdateStatus(status);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(1, events.Count);
        var statusChanged = events[0] as StatusChanged;
        Assert.IsNotNull(statusChanged);
        Assert.AreEqual(Sec, statusChanged.Security);
        Assert.AreEqual(status, statusChanged.Status);
        Assert.AreEqual(Now1, statusChanged.Time);
    }

    [Test]
    public void Open_MatchPreOpenOrders()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.PreOpen);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 5, 100);
        Clock.SetCurrentTime(Now2);
        Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Sell, 5, 100);
        Clock.SetCurrentTime(Now3);

        // act
        var events = Book.UpdateStatus(OrderBookStatus.Open);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(3, events.Count);
        var withdrawn = events[2] as IndicativePriceChanged;
        Assert.IsNotNull(withdrawn);
        Assert.IsNull(withdrawn.Price, "the auction it was quoting has printed");
        var matched = events[1] as OrdersMatched;
        Assert.IsNotNull(matched);
        Assert.AreEqual(Sec, matched.Security);
        Assert.AreEqual(Now3, matched.Time);
        Assert.AreEqual(100, matched.Price);
        Assert.AreEqual(5, matched.Quantity);
        Assert.IsNotNull(matched.Fills);
        Assert.AreEqual(2, matched.Fills.Count);

        Assert.AreEqual(Sec, matched.Fills[0].Security);
        Assert.AreEqual(Now3, matched.Fills[0].Time);
        Assert.AreEqual(CompanyId1, matched.Fills[0].CompanyId);
        Assert.AreEqual(OrderId1, matched.Fills[0].ClientOrderId);
        Assert.AreEqual(100, matched.Fills[0].Price);
        Assert.AreEqual(5, matched.Fills[0].Quantity);
        Assert.AreEqual(true, matched.Fills[0].IsResting);
        Assert.AreEqual(CompanyId1, matched.Fills[0].Order.CompanyId);
        Assert.AreEqual(OrderId1, matched.Fills[0].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched.Fills[0].Order.Security);
        Assert.AreEqual(Now1, matched.Fills[0].Order.CreatedTime);
        Assert.AreEqual(Now1, matched.Fills[0].Order.ModifiedTime);
        Assert.AreEqual(Now3, matched.Fills[0].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Filled, matched.Fills[0].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched.Fills[0].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched.Fills[0].Order.OrderValidity);
        Assert.AreEqual(Side.Buy, matched.Fills[0].Order.Side);
        Assert.AreEqual(100, matched.Fills[0].Order.Price);
        Assert.IsNull(matched.Fills[0].Order.TriggerPrice);
        Assert.AreEqual(5, matched.Fills[0].Order.Quantity);
        Assert.AreEqual(5, matched.Fills[0].Order.FilledQuantity);
        Assert.AreEqual(0, matched.Fills[0].Order.RemainingQuantity);

        Assert.AreEqual(Sec, matched.Fills[1].Security);
        Assert.AreEqual(Now3, matched.Fills[1].Time);
        Assert.AreEqual(CompanyId2, matched.Fills[1].CompanyId);
        Assert.AreEqual(OrderId2, matched.Fills[1].ClientOrderId);
        Assert.AreEqual(100, matched.Fills[1].Price);
        Assert.AreEqual(5, matched.Fills[1].Quantity);
        Assert.AreEqual(false, matched.Fills[1].IsResting);
        Assert.AreEqual(CompanyId2, matched.Fills[1].Order.CompanyId);
        Assert.AreEqual(OrderId2, matched.Fills[1].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched.Fills[1].Order.Security);
        Assert.AreEqual(Now2, matched.Fills[1].Order.CreatedTime);
        Assert.AreEqual(Now2, matched.Fills[1].Order.ModifiedTime);
        Assert.AreEqual(Now3, matched.Fills[1].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Filled, matched.Fills[1].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched.Fills[1].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched.Fills[1].Order.OrderValidity);
        Assert.AreEqual(Side.Sell, matched.Fills[1].Order.Side);
        Assert.AreEqual(100, matched.Fills[1].Order.Price);
        Assert.IsNull(matched.Fills[1].Order.TriggerPrice);
        Assert.AreEqual(5, matched.Fills[1].Order.Quantity);
        Assert.AreEqual(5, matched.Fills[1].Order.FilledQuantity);
        Assert.AreEqual(0, matched.Fills[1].Order.RemainingQuantity);
    }

    [Test]
    public void Closed_ExpireDayLimitOrders()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 5, 100);
        Clock.SetCurrentTime(Now2);

        // act
        var events = Book.UpdateStatus(OrderBookStatus.Closed);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(2, events.Count);

        var expired = events[1] as ExpireOrderConfirmed;
        Assert.IsNotNull(expired);
        Assert.AreEqual(Sec, expired.Security);
        Assert.AreEqual(Now2, expired.Time);
        Assert.AreEqual(CompanyId1, expired.CompanyId);
        Assert.AreEqual(100, expired.PreviousPrice, "the working-book price it's being removed from");
        Assert.AreEqual(5, expired.PreviousQuantity, "the working-book displayed quantity being removed");
        Assert.AreEqual(CompanyId1, expired.Order.CompanyId);
        Assert.AreEqual(OrderId1, expired.Order.ClientOrderId);
        Assert.AreEqual(Sec, expired.Order.Security);
        Assert.AreEqual(Now1, expired.Order.CreatedTime);
        Assert.AreEqual(Now1, expired.Order.ModifiedTime);
        Assert.AreEqual(Now2, expired.Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Expired, expired.Order.Status);
        Assert.AreEqual(OrderType.Limit, expired.Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), expired.Order.OrderValidity);
        Assert.AreEqual(Side.Buy, expired.Order.Side);
        Assert.AreEqual(100, expired.Order.Price);
        Assert.IsNull(expired.Order.TriggerPrice);
        Assert.AreEqual(5, expired.Order.Quantity);
        Assert.AreEqual(0, expired.Order.FilledQuantity);
        Assert.AreEqual(0, expired.Order.RemainingQuantity);
    }

    [Test]
    public void Closed_ExpireDayStopOrders()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 5, 100);
        Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Sell, 5, 100);
        Book.CreateStopMarketOrder(CompanyId3, OrderId3, new OrderValidity.Day(), Side.Sell, 5, 90);
        Clock.SetCurrentTime(Now2);

        // act
        var events = Book.UpdateStatus(OrderBookStatus.Closed);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(2, events.Count);

        var expired = events[1] as ExpireOrderConfirmed;
        Assert.IsNotNull(expired);
        Assert.AreEqual(Sec, expired.Security);
        Assert.AreEqual(Now2, expired.Time);
        Assert.AreEqual(CompanyId3, expired.CompanyId);
        Assert.IsNull(expired.PreviousPrice, "still Hidden - never resting in the working book");
        Assert.AreEqual(5, expired.PreviousQuantity, "the working-book displayed quantity being removed");
        Assert.AreEqual(CompanyId3, expired.Order.CompanyId);
        Assert.AreEqual(OrderId3, expired.Order.ClientOrderId);
        Assert.AreEqual(Sec, expired.Order.Security);
        Assert.AreEqual(Now1, expired.Order.CreatedTime);
        Assert.AreEqual(Now1, expired.Order.ModifiedTime);
        Assert.AreEqual(Now2, expired.Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Expired, expired.Order.Status);
        Assert.AreEqual(OrderType.StopMarket, expired.Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), expired.Order.OrderValidity);
        Assert.AreEqual(Side.Sell, expired.Order.Side);
        Assert.IsNull(expired.Order.Price);
        Assert.AreEqual(90, expired.Order.TriggerPrice);
        Assert.AreEqual(5, expired.Order.Quantity);
        Assert.AreEqual(0, expired.Order.FilledQuantity);
        Assert.AreEqual(0, expired.Order.RemainingQuantity);
    }

    [Test]
    public void Closed_DontExpireGoodTilCanceledOrders()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.GoodTilCanceled(), Side.Buy, 5, 100);
        Clock.SetCurrentTime(Now2);

        // act
        var events = Book.UpdateStatus(OrderBookStatus.Closed);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(1, events.Count);

        var statusChanged = events[0] as StatusChanged;
        Assert.IsNotNull(statusChanged);
        Assert.AreEqual(Sec, statusChanged.Security);
        Assert.AreEqual(OrderBookStatus.Closed, statusChanged.Status);
        Assert.AreEqual(Now2, statusChanged.Time);
    }

    [Test]
    public void Closed_DontExpireGoodTilDateOrdersBeforeDate()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        var goodTilDate = DateOnly.FromDateTime(Now1).AddDays(1);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.GoodTilDate { Date = goodTilDate }, Side.Buy, 5, 100);
        Clock.SetCurrentTime(Now2);

        // act
        var events = Book.UpdateStatus(OrderBookStatus.Closed);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(1, events.Count);

        var statusChanged = events[0] as StatusChanged;
        Assert.IsNotNull(statusChanged);
        Assert.AreEqual(Sec, statusChanged.Security);
        Assert.AreEqual(OrderBookStatus.Closed, statusChanged.Status);
        Assert.AreEqual(Now2, statusChanged.Time);
    }

    [Test]
    public void Closed_ExpireGoodTilDateOrdersOnDate()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        var goodTilDate = DateOnly.FromDateTime(Now1);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.GoodTilDate { Date = goodTilDate }, Side.Buy, 5, 100);
        Clock.SetCurrentTime(Now2);

        // act
        var events = Book.UpdateStatus(OrderBookStatus.Closed);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(2, events.Count);

        var expired = events[1] as ExpireOrderConfirmed;
        Assert.IsNotNull(expired);
        Assert.AreEqual(Sec, expired.Security);
        Assert.AreEqual(Now2, expired.Time);
        Assert.AreEqual(CompanyId1, expired.CompanyId);
        Assert.AreEqual(CompanyId1, expired.Order.CompanyId);
        Assert.AreEqual(OrderId1, expired.Order.ClientOrderId);
        Assert.AreEqual(OrderStatus.Expired, expired.Order.Status);
        Assert.AreEqual(new OrderValidity.GoodTilDate { Date = goodTilDate }, expired.Order.OrderValidity);
    }

    [Test]
    public void Closed_ExpireGoodTilDateOrdersOnceDateReachedAcrossSessions()
    {
        // arrange - order survives close on days before its good-til-date, then expires
        // on the close of the session where the date is reached
        Book.UpdateStatus(OrderBookStatus.Open);
        var goodTilDate = DateOnly.FromDateTime(Now1).AddDays(2);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.GoodTilDate { Date = goodTilDate }, Side.Buy, 5, 100);
        Book.UpdateStatus(OrderBookStatus.Closed); // day 1 close, not due

        var day2 = Now1.AddDays(1);
        Clock.SetCurrentTime(day2);
        Book.UpdateStatus(OrderBookStatus.PreOpen);
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.UpdateStatus(OrderBookStatus.Closed); // day 2 close, still not due

        var day3 = Now1.AddDays(2);
        Clock.SetCurrentTime(day3);
        Book.UpdateStatus(OrderBookStatus.PreOpen);
        Book.UpdateStatus(OrderBookStatus.Open);

        // act
        var events = Book.UpdateStatus(OrderBookStatus.Closed); // day 3 close, due

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(2, events.Count);

        var expired = events[1] as ExpireOrderConfirmed;
        Assert.IsNotNull(expired);
        Assert.AreEqual(Sec, expired.Security);
        Assert.AreEqual(day3, expired.Time);
        Assert.AreEqual(CompanyId1, expired.CompanyId);
        Assert.AreEqual(OrderId1, expired.Order.ClientOrderId);
        Assert.AreEqual(OrderStatus.Expired, expired.Order.Status);
        Assert.AreEqual(new OrderValidity.GoodTilDate { Date = goodTilDate }, expired.Order.OrderValidity);
    }
}
