using Circus.Actions;
using Circus.Events;
using Circus.Time;
using NUnit.Framework;

namespace Circus.Tests.Orders;

[TestFixture]
public class CancelOrderTests
{
    private static readonly Instrument Sec = new("GCZ6", 10, 10);

    private static readonly DateTime Now1 = new(2000, 1, 1, 12, 0, 0);
    private static readonly DateTime Now2 = new(2000, 1, 1, 12, 1, 0);

    private static readonly string CompanyId1 = "Company1";
    private static readonly string CompanyId2 = "Company2";
    private static readonly string CompanyId3 = "Company3";

    private static readonly string OrderId1 = "Order1";
    private static readonly string OrderId2 = "Order2";
    private static readonly string OrderId3 = "Order3";
    private static readonly string OrderId4 = "Order4";
    private static readonly string OrderId5 = "Order5";

    private static ManualClock Clock;
    private static IOrderBook Book;

    [SetUp]
    public void SetUp()
    {
        Clock = new ManualClock(Now1);
        Book = new TimestampingOrderBook(Sec, Clock);
    }

    [Test]
    public void LimitOrder_Success()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 3, 100);
        Clock.SetCurrentTime(Now2);

        // act
        var events = Book.CancelOrder(CompanyId1, OrderId4, OrderId1);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(1, events.Count);
        var cancelled = events[0] as CancelOrderConfirmed;
        Assert.IsNotNull(cancelled);
        Assert.AreEqual(Sec.Symbol, cancelled.Symbol);
        Assert.AreEqual(Now2, cancelled.Time);
        Assert.AreEqual(CompanyId1, cancelled.CompanyId);
        Assert.AreEqual(OrderId1, cancelled.PreviousClientOrderId);
        Assert.AreEqual(OrderCancelledReason.Cancelled, cancelled.Reason);
        Assert.AreEqual(100, cancelled.PreviousPrice, "the working-book price it's being removed from");
        Assert.AreEqual(3, cancelled.PreviousQuantity, "the working-book displayed quantity being removed");
        Assert.AreEqual(CompanyId1, cancelled.Order.CompanyId);
        Assert.AreEqual(OrderId4, cancelled.Order.ClientOrderId);
        Assert.AreEqual(Sec, cancelled.Order.Instrument);
        Assert.AreEqual(Now1, cancelled.Order.CreatedTime);
        Assert.AreEqual(Now1, cancelled.Order.ModifiedTime);
        Assert.AreEqual(Now2, cancelled.Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Cancelled, cancelled.Order.Status);
        Assert.AreEqual(OrderType.Limit, cancelled.Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), cancelled.Order.OrderValidity);
        Assert.AreEqual(Side.Buy, cancelled.Order.Side);
        Assert.AreEqual(100, cancelled.Order.Price);
        Assert.IsNull(cancelled.Order.TriggerPrice);
        Assert.AreEqual(3, cancelled.Order.Quantity);
        Assert.AreEqual(0, cancelled.Order.FilledQuantity);
        Assert.AreEqual(0, cancelled.Order.RemainingQuantity);
    }

    [Test]
    public void StopOrder_Success()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 5, 100);
        Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Sell, 5, 100);
        Book.CreateStopMarketOrder(CompanyId3, OrderId3, new OrderValidity.Day(), Side.Buy, 3, 110);
        Clock.SetCurrentTime(Now2);

        // act
        var events = Book.CancelOrder(CompanyId3, OrderId4, OrderId3);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(1, events.Count);
        var cancelled = events[0] as CancelOrderConfirmed;
        Assert.IsNotNull(cancelled);
        Assert.AreEqual(Sec.Symbol, cancelled.Symbol);
        Assert.AreEqual(Now2, cancelled.Time);
        Assert.AreEqual(CompanyId3, cancelled.CompanyId);
        Assert.AreEqual(OrderId3, cancelled.PreviousClientOrderId);
        Assert.AreEqual(OrderCancelledReason.Cancelled, cancelled.Reason);
        Assert.IsNull(cancelled.PreviousPrice, "still Hidden - never resting in the working book");
        Assert.AreEqual(3, cancelled.PreviousQuantity, "the working-book displayed quantity being removed");
        Assert.AreEqual(CompanyId3, cancelled.Order.CompanyId);
        Assert.AreEqual(OrderId4, cancelled.Order.ClientOrderId);
        Assert.AreEqual(Sec, cancelled.Order.Instrument);
        Assert.AreEqual(Now1, cancelled.Order.CreatedTime);
        Assert.AreEqual(Now1, cancelled.Order.ModifiedTime);
        Assert.AreEqual(Now2, cancelled.Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Cancelled, cancelled.Order.Status);
        Assert.AreEqual(OrderType.StopMarket, cancelled.Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), cancelled.Order.OrderValidity);
        Assert.AreEqual(Side.Buy, cancelled.Order.Side);
        Assert.IsNull(cancelled.Order.Price);
        Assert.AreEqual(110, cancelled.Order.TriggerPrice);
        Assert.AreEqual(3, cancelled.Order.Quantity);
        Assert.AreEqual(0, cancelled.Order.FilledQuantity);
        Assert.AreEqual(0, cancelled.Order.RemainingQuantity);
    }

    [Test]
    public void MarketClosed_Rejected()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 3, 100);
        Book.UpdateStatus(OrderBookStatus.Closed);

        // act
        var events = Book.CancelOrder(CompanyId1, OrderId4, OrderId1);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(1, events.Count);
        var rejected = events[0] as CancelOrderRejected;
        Assert.IsNotNull(rejected);
        Assert.AreEqual(Sec.Symbol, rejected.Symbol);
        Assert.AreEqual(Now1, rejected.Time);
        Assert.AreEqual(CompanyId1, rejected.CompanyId);
        Assert.AreEqual(OrderId4, rejected.ClientOrderId);
        Assert.AreEqual(OrderId1, rejected.PreviousClientOrderId);
        Assert.AreEqual(OrderRejectedReason.MarketClosed, rejected.Reason);
        Assert.IsNull(rejected.ExchangeOrderId, "rejected before the order was ever looked up");
    }

    [Test]
    public void Completed_Rejected()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        var created = Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 3, 100)
            .OfType<CreateOrderConfirmed>().Single();
        Book.CancelOrder(CompanyId1, OrderId4, OrderId1);

        // act
        var events = Book.CancelOrder(CompanyId1, OrderId5, OrderId4);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(1, events.Count);
        var rejected = events[0] as CancelOrderRejected;
        Assert.IsNotNull(rejected);
        Assert.AreEqual(Sec.Symbol, rejected.Symbol);
        Assert.AreEqual(Now1, rejected.Time);
        Assert.AreEqual(CompanyId1, rejected.CompanyId);
        Assert.AreEqual(OrderId5, rejected.ClientOrderId);
        Assert.AreEqual(OrderId4, rejected.PreviousClientOrderId);
        Assert.AreEqual(OrderRejectedReason.TooLateToCancel, rejected.Reason);
        Assert.AreEqual(created.Order.ExchangeOrderId, rejected.ExchangeOrderId,
            "the order was found (and is now cancelled), so its ExchangeOrderId is known");
    }

    [Test]
    public void NotFound_Rejected()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);

        // act
        var events = Book.CancelOrder(CompanyId1, OrderId4, OrderId1);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(1, events.Count);
        var rejected = events[0] as CancelOrderRejected;
        Assert.IsNotNull(rejected);
        Assert.AreEqual(Sec.Symbol, rejected.Symbol);
        Assert.AreEqual(Now1, rejected.Time);
        Assert.AreEqual(CompanyId1, rejected.CompanyId);
        Assert.AreEqual(OrderId4, rejected.ClientOrderId);
        Assert.AreEqual(OrderId1, rejected.PreviousClientOrderId);
        Assert.AreEqual(OrderRejectedReason.OrderNotInBook, rejected.Reason);
    }

    [Test]
    public void ForeignClientOrderId_Rejected()
    {
        // arrange - client 2 cannot cancel client 1's order by quoting client 1's clientOrderId,
        // since the (companyId, clientOrderId) lookup is scoped per client
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 3, 100);

        // act
        var events = Book.CancelOrder(CompanyId2, OrderId4, OrderId1);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(1, events.Count);
        var rejected = events[0] as CancelOrderRejected;
        Assert.IsNotNull(rejected);
        Assert.AreEqual(CompanyId2, rejected.CompanyId);
        Assert.AreEqual(OrderRejectedReason.OrderNotInBook, rejected.Reason);

        // the order itself is untouched and still cancellable by its actual owner
        var ownerEvents = Book.CancelOrder(CompanyId1, OrderId5, OrderId1);
        Assert.IsInstanceOf<CancelOrderConfirmed>(ownerEvents[0]);
    }
}
