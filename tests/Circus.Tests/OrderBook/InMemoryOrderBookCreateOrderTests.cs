using Circus.OrderBook;
using Circus.OrderBook.Actions;
using Circus.OrderBook.Events;
using Circus.TimeProviders;
using NUnit.Framework;

namespace Circus.Tests.OrderBook;

[TestFixture]
public class InMemoryOrderBookCreateOrderTests
{
    private static readonly Security Sec = new("GCZ6", SecurityType.Future, 10, 10);

    private static readonly DateTime Now1 = new(2000, 1, 1, 12, 0, 0);
    private static readonly DateTime Now2 = new(2000, 1, 1, 12, 1, 0);
    private static readonly DateTime Now3 = new(2000, 1, 1, 12, 2, 0);
    private static readonly DateTime Now4 = new(2000, 1, 1, 12, 3, 0);

    private static readonly string CompanyId1 = "Company1";
    private static readonly string CompanyId2 = "Company2";
    private static readonly string CompanyId3 = "Company3";

    private static readonly string OrderId1 = "Order1";
    private static readonly string OrderId2 = "Order2";
    private static readonly string OrderId3 = "Order3";
    private static readonly string OrderId4 = "Order4";
    private static readonly string OrderId5 = "Order5";

    private static TestTimeProvider TimeProvider;
    private static IOrderBook Book;

    [SetUp]
    public void SetUp()
    {
        TimeProvider = new TestTimeProvider(Now1);
        Book = new InMemoryOrderBook(Sec, TimeProvider);
    }

    [TestCase(Side.Buy)]
    [TestCase(Side.Sell)]
    public void LimitOrder_Success(Side side)
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);

        // act
        var events = Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), side, 3, 100);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(1, events.Count);

        var created = events[0] as CreateOrderConfirmed;
        Assert.IsNotNull(created);
        Assert.AreEqual(Sec, created.Security);
        Assert.AreEqual(Now1, created.Time);
        Assert.AreEqual(CompanyId1, created.CompanyId);
        Assert.AreEqual(created.Order.ExchangeOrderId, created.ExchangeOrderId);
        Assert.AreEqual(CompanyId1, created.Order.CompanyId);
        Assert.AreEqual(OrderId1, created.Order.ClientOrderId);
        Assert.AreEqual(Sec, created.Order.Security);
        Assert.AreEqual(Now1, created.Order.CreatedTime);
        Assert.AreEqual(Now1, created.Order.ModifiedTime);
        Assert.IsNull(created.Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Working, created.Order.Status);
        Assert.AreEqual(OrderType.Limit, created.Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), created.Order.OrderValidity);
        Assert.AreEqual(side, created.Order.Side);
        Assert.AreEqual(100, created.Order.Price);
        Assert.IsNull(created.Order.TriggerPrice);
        Assert.AreEqual(3, created.Order.Quantity);
        Assert.AreEqual(0, created.Order.FilledQuantity);
        Assert.AreEqual(3, created.Order.RemainingQuantity);
    }

    [TestCase(Side.Buy, 700)]
    [TestCase(Side.Sell, 300)]
    public void MarketOrder_Success(Side side, decimal limitPrice)
    {
        // arrange
        var sec = new Security("GCZ6", SecurityType.Future, 10, 10, 20);
        var book = new InMemoryOrderBook(sec, TimeProvider);
        book.UpdateStatus(OrderBookStatus.Open);
        book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), side == Side.Buy ? Side.Sell : Side.Buy, 3, 500);
        TimeProvider.SetCurrentTime(Now2);

        // act
        var events = book.CreateMarketOrder(CompanyId2, OrderId2, new OrderValidity.Day(), side, 5);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(2, events.Count);

        var created = events[0] as CreateOrderConfirmed;
        Assert.IsNotNull(created);
        Assert.AreEqual(sec, created.Security);
        Assert.AreEqual(Now2, created.Time);
        Assert.AreEqual(CompanyId2, created.CompanyId);
        Assert.AreEqual(CompanyId2, created.Order.CompanyId);
        Assert.AreEqual(OrderId2, created.Order.ClientOrderId);
        Assert.AreEqual(sec, created.Order.Security);
        Assert.AreEqual(Now2, created.Order.CreatedTime);
        Assert.AreEqual(Now2, created.Order.ModifiedTime);
        Assert.IsNull(created.Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Working, created.Order.Status);
        Assert.AreEqual(OrderType.Market, created.Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), created.Order.OrderValidity);
        Assert.AreEqual(side, created.Order.Side);
        Assert.AreEqual(limitPrice, created.Order.Price);
        Assert.IsNull(created.Order.TriggerPrice);
        Assert.AreEqual(5, created.Order.Quantity);
        Assert.AreEqual(0, created.Order.FilledQuantity);
        Assert.AreEqual(5, created.Order.RemainingQuantity);

        var matched = events[1] as OrdersMatched;
        Assert.IsNotNull(matched);
        Assert.AreEqual(sec, matched.Security);
        Assert.AreEqual(Now2, matched.Time);
        Assert.AreEqual(500, matched.Price);
        Assert.AreEqual(3, matched.Quantity);
        Assert.IsNotNull(matched.Fills);
        Assert.AreEqual(2, matched.Fills.Count);

        Assert.AreEqual(sec, matched.Fills[0].Security);
        Assert.AreEqual(Now2, matched.Fills[0].Time);
        Assert.AreEqual(CompanyId1, matched.Fills[0].CompanyId);
        Assert.AreEqual(OrderId1, matched.Fills[0].ClientOrderId);
        Assert.AreEqual(500, matched.Fills[0].Price);
        Assert.AreEqual(3, matched.Fills[0].Quantity);
        Assert.AreEqual(true, matched.Fills[0].IsResting);
        Assert.AreEqual(CompanyId1, matched.Fills[0].Order.CompanyId);
        Assert.AreEqual(OrderId1, matched.Fills[0].Order.ClientOrderId);
        Assert.AreEqual(sec, matched.Fills[0].Order.Security);
        Assert.AreEqual(Now1, matched.Fills[0].Order.CreatedTime);
        Assert.AreEqual(Now1, matched.Fills[0].Order.ModifiedTime);
        Assert.AreEqual(Now2, matched.Fills[0].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Filled, matched.Fills[0].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched.Fills[0].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched.Fills[0].Order.OrderValidity);
        Assert.AreEqual(side == Side.Buy ? Side.Sell : Side.Buy, matched.Fills[0].Order.Side);
        Assert.AreEqual(500, matched.Fills[0].Order.Price);
        Assert.IsNull(matched.Fills[0].Order.TriggerPrice);
        Assert.AreEqual(3, matched.Fills[0].Order.Quantity);
        Assert.AreEqual(3, matched.Fills[0].Order.FilledQuantity);
        Assert.AreEqual(0, matched.Fills[0].Order.RemainingQuantity);

        Assert.AreEqual(sec, matched.Fills[1].Security);
        Assert.AreEqual(Now2, matched.Fills[1].Time);
        Assert.AreEqual(CompanyId2, matched.Fills[1].CompanyId);
        Assert.AreEqual(OrderId2, matched.Fills[1].ClientOrderId);
        Assert.AreEqual(500, matched.Fills[1].Price);
        Assert.AreEqual(3, matched.Fills[1].Quantity);
        Assert.AreEqual(false, matched.Fills[1].IsResting);
        Assert.AreEqual(CompanyId2, matched.Fills[1].Order.CompanyId);
        Assert.AreEqual(OrderId2, matched.Fills[1].Order.ClientOrderId);
        Assert.AreEqual(sec, matched.Fills[1].Order.Security);
        Assert.AreEqual(Now2, matched.Fills[1].Order.CreatedTime);
        Assert.AreEqual(Now2, matched.Fills[1].Order.ModifiedTime);
        Assert.IsNull(matched.Fills[1].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Working, matched.Fills[1].Order.Status);
        Assert.AreEqual(OrderType.Market, matched.Fills[1].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched.Fills[1].Order.OrderValidity);
        Assert.AreEqual(side, matched.Fills[1].Order.Side);
        Assert.AreEqual(limitPrice, matched.Fills[1].Order.Price);
        Assert.IsNull(matched.Fills[1].Order.TriggerPrice);
        Assert.AreEqual(5, matched.Fills[1].Order.Quantity);
        Assert.AreEqual(3, matched.Fills[1].Order.FilledQuantity);
        Assert.AreEqual(2, matched.Fills[1].Order.RemainingQuantity);
    }

    [TestCase(Side.Buy, 520, 510)]
    [TestCase(Side.Sell, 490, 490)]
    public void StopLimitOrder_Success(Side side, decimal price, decimal triggerPrice)
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 3, 500);
        Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Sell, 2, 500);
        TimeProvider.SetCurrentTime(Now2);

        // act
        var events = Book.CreateStopLimitOrder(CompanyId3, OrderId3, new OrderValidity.Day(), side, 5, price, triggerPrice);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(1, events.Count);

        var created = events[0] as CreateOrderConfirmed;
        Assert.IsNotNull(created);
        Assert.AreEqual(Sec, created.Security);
        Assert.AreEqual(Now2, created.Time);
        Assert.AreEqual(CompanyId3, created.CompanyId);
        Assert.AreEqual(CompanyId3, created.Order.CompanyId);
        Assert.AreEqual(OrderId3, created.Order.ClientOrderId);
        Assert.AreEqual(Sec, created.Order.Security);
        Assert.AreEqual(Now2, created.Order.CreatedTime);
        Assert.AreEqual(Now2, created.Order.ModifiedTime);
        Assert.IsNull(created.Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Hidden, created.Order.Status);
        Assert.AreEqual(OrderType.StopLimit, created.Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), created.Order.OrderValidity);
        Assert.AreEqual(side, created.Order.Side);
        Assert.AreEqual(price, created.Order.Price);
        Assert.AreEqual(triggerPrice, created.Order.TriggerPrice);
        Assert.AreEqual(5, created.Order.Quantity);
        Assert.AreEqual(0, created.Order.FilledQuantity);
        Assert.AreEqual(5, created.Order.RemainingQuantity);
    }

    [TestCase(Side.Buy, 510)]
    [TestCase(Side.Sell, 490)]
    public void StopMarketOrder_Success(Side side, decimal triggerPrice)
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 3, 500);
        Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Sell, 2, 500);
        TimeProvider.SetCurrentTime(Now2);

        // act
        var events = Book.CreateStopMarketOrder(CompanyId3, OrderId3, new OrderValidity.Day(), side, 5, triggerPrice);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(1, events.Count);

        var created = events[0] as CreateOrderConfirmed;
        Assert.IsNotNull(created);
        Assert.AreEqual(Sec, created.Security);
        Assert.AreEqual(Now2, created.Time);
        Assert.AreEqual(CompanyId3, created.CompanyId);
        Assert.AreEqual(CompanyId3, created.Order.CompanyId);
        Assert.AreEqual(OrderId3, created.Order.ClientOrderId);
        Assert.AreEqual(Sec, created.Order.Security);
        Assert.AreEqual(Now2, created.Order.CreatedTime);
        Assert.AreEqual(Now2, created.Order.ModifiedTime);
        Assert.IsNull(created.Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Hidden, created.Order.Status);
        Assert.AreEqual(OrderType.StopMarket, created.Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), created.Order.OrderValidity);
        Assert.AreEqual(side, created.Order.Side);
        Assert.IsNull(created.Order.Price);
        Assert.AreEqual(triggerPrice, created.Order.TriggerPrice);
        Assert.AreEqual(5, created.Order.Quantity);
        Assert.AreEqual(0, created.Order.FilledQuantity);
        Assert.AreEqual(5, created.Order.RemainingQuantity);
    }

    [Test]
    public void LimitOrder_MatchAtSamePriceWithAggressorRemaining_Success()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 3, 100);
        TimeProvider.SetCurrentTime(Now2);

        // act
        var events = Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Sell, 5, 100);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(2, events.Count);

        var matched = events[1] as OrdersMatched;
        Assert.IsNotNull(matched);
        Assert.AreEqual(Sec, matched.Security);
        Assert.AreEqual(Now2, matched.Time);
        Assert.AreEqual(100, matched.Price);
        Assert.AreEqual(3, matched.Quantity);
        Assert.IsNotNull(matched.Fills);
        Assert.AreEqual(2, matched.Fills.Count);

        Assert.AreEqual(Sec, matched.Fills[0].Security);
        Assert.AreEqual(Now2, matched.Fills[0].Time);
        Assert.AreEqual(CompanyId1, matched.Fills[0].CompanyId);
        Assert.AreEqual(OrderId1, matched.Fills[0].ClientOrderId);
        Assert.AreEqual(100, matched.Fills[0].Price);
        Assert.AreEqual(3, matched.Fills[0].Quantity);
        Assert.AreEqual(true, matched.Fills[0].IsResting);
        Assert.AreEqual(CompanyId1, matched.Fills[0].Order.CompanyId);
        Assert.AreEqual(OrderId1, matched.Fills[0].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched.Fills[0].Order.Security);
        Assert.AreEqual(Now1, matched.Fills[0].Order.CreatedTime);
        Assert.AreEqual(Now1, matched.Fills[0].Order.ModifiedTime);
        Assert.AreEqual(Now2, matched.Fills[0].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Filled, matched.Fills[0].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched.Fills[0].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched.Fills[0].Order.OrderValidity);
        Assert.AreEqual(Side.Buy, matched.Fills[0].Order.Side);
        Assert.AreEqual(100, matched.Fills[0].Order.Price);
        Assert.IsNull(matched.Fills[0].Order.TriggerPrice);
        Assert.AreEqual(3, matched.Fills[0].Order.Quantity);
        Assert.AreEqual(3, matched.Fills[0].Order.FilledQuantity);
        Assert.AreEqual(0, matched.Fills[0].Order.RemainingQuantity);

        Assert.AreEqual(Sec, matched.Fills[1].Security);
        Assert.AreEqual(Now2, matched.Fills[1].Time);
        Assert.AreEqual(CompanyId2, matched.Fills[1].CompanyId);
        Assert.AreEqual(OrderId2, matched.Fills[1].ClientOrderId);
        Assert.AreEqual(100, matched.Fills[1].Price);
        Assert.AreEqual(3, matched.Fills[1].Quantity);
        Assert.AreEqual(false, matched.Fills[1].IsResting);
        Assert.AreEqual(CompanyId2, matched.Fills[1].Order.CompanyId);
        Assert.AreEqual(OrderId2, matched.Fills[1].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched.Fills[1].Order.Security);
        Assert.AreEqual(Now2, matched.Fills[1].Order.CreatedTime);
        Assert.AreEqual(Now2, matched.Fills[1].Order.ModifiedTime);
        Assert.IsNull(matched.Fills[1].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Working, matched.Fills[1].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched.Fills[1].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched.Fills[1].Order.OrderValidity);
        Assert.AreEqual(Side.Sell, matched.Fills[1].Order.Side);
        Assert.AreEqual(100, matched.Fills[1].Order.Price);
        Assert.IsNull(matched.Fills[1].Order.TriggerPrice);
        Assert.AreEqual(5, matched.Fills[1].Order.Quantity);
        Assert.AreEqual(3, matched.Fills[1].Order.FilledQuantity);
        Assert.AreEqual(2, matched.Fills[1].Order.RemainingQuantity);
    }

    [Test]
    public void LimitOrder_MatchAtDifferentPriceWithAggressorRemaining_Success()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 3, 110);
        TimeProvider.SetCurrentTime(Now2);

        // act
        var events = Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Sell, 5, 100);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(2, events.Count);

        var matched = events[1] as OrdersMatched;
        Assert.IsNotNull(matched);
        Assert.AreEqual(Sec, matched.Security);
        Assert.AreEqual(Now2, matched.Time);
        Assert.AreEqual(110, matched.Price);
        Assert.AreEqual(3, matched.Quantity);
        Assert.IsNotNull(matched.Fills);
        Assert.AreEqual(2, matched.Fills.Count);

        Assert.AreEqual(Sec, matched.Fills[0].Security);
        Assert.AreEqual(Now2, matched.Fills[0].Time);
        Assert.AreEqual(CompanyId1, matched.Fills[0].CompanyId);
        Assert.AreEqual(OrderId1, matched.Fills[0].ClientOrderId);
        Assert.AreEqual(110, matched.Fills[0].Price);
        Assert.AreEqual(3, matched.Fills[0].Quantity);
        Assert.AreEqual(true, matched.Fills[0].IsResting);
        Assert.AreEqual(CompanyId1, matched.Fills[0].Order.CompanyId);
        Assert.AreEqual(OrderId1, matched.Fills[0].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched.Fills[0].Order.Security);
        Assert.AreEqual(Now1, matched.Fills[0].Order.CreatedTime);
        Assert.AreEqual(Now1, matched.Fills[0].Order.ModifiedTime);
        Assert.AreEqual(Now2, matched.Fills[0].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Filled, matched.Fills[0].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched.Fills[0].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched.Fills[0].Order.OrderValidity);
        Assert.AreEqual(Side.Buy, matched.Fills[0].Order.Side);
        Assert.AreEqual(110, matched.Fills[0].Order.Price);
        Assert.IsNull(matched.Fills[0].Order.TriggerPrice);
        Assert.AreEqual(3, matched.Fills[0].Order.Quantity);
        Assert.AreEqual(3, matched.Fills[0].Order.FilledQuantity);
        Assert.AreEqual(0, matched.Fills[0].Order.RemainingQuantity);

        Assert.AreEqual(Sec, matched.Fills[1].Security);
        Assert.AreEqual(Now2, matched.Fills[1].Time);
        Assert.AreEqual(CompanyId2, matched.Fills[1].CompanyId);
        Assert.AreEqual(OrderId2, matched.Fills[1].ClientOrderId);
        Assert.AreEqual(110, matched.Fills[1].Price);
        Assert.AreEqual(3, matched.Fills[1].Quantity);
        Assert.AreEqual(false, matched.Fills[1].IsResting);
        Assert.AreEqual(CompanyId2, matched.Fills[1].Order.CompanyId);
        Assert.AreEqual(OrderId2, matched.Fills[1].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched.Fills[1].Order.Security);
        Assert.AreEqual(Now2, matched.Fills[1].Order.CreatedTime);
        Assert.AreEqual(Now2, matched.Fills[1].Order.ModifiedTime);
        Assert.IsNull(matched.Fills[1].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Working, matched.Fills[1].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched.Fills[1].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched.Fills[1].Order.OrderValidity);
        Assert.AreEqual(Side.Sell, matched.Fills[1].Order.Side);
        Assert.AreEqual(100, matched.Fills[1].Order.Price);
        Assert.IsNull(matched.Fills[1].Order.TriggerPrice);
        Assert.AreEqual(5, matched.Fills[1].Order.Quantity);
        Assert.AreEqual(3, matched.Fills[1].Order.FilledQuantity);
        Assert.AreEqual(2, matched.Fills[1].Order.RemainingQuantity);
    }

    [Test]
    public void LimitOrder_MatchAtDifferentPriceWithRestingRemaining_Success()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 5, 110);
        TimeProvider.SetCurrentTime(Now2);

        // act
        var events = Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Sell, 3, 100);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(2, events.Count);

        var matched = events[1] as OrdersMatched;
        Assert.IsNotNull(matched);
        Assert.AreEqual(Sec, matched.Security);
        Assert.AreEqual(Now2, matched.Time);
        Assert.AreEqual(110, matched.Price);
        Assert.AreEqual(3, matched.Quantity);
        Assert.IsNotNull(matched.Fills);
        Assert.AreEqual(2, matched.Fills.Count);

        Assert.AreEqual(Sec, matched.Fills[0].Security);
        Assert.AreEqual(Now2, matched.Fills[0].Time);
        Assert.AreEqual(CompanyId1, matched.Fills[0].CompanyId);
        Assert.AreEqual(OrderId1, matched.Fills[0].ClientOrderId);
        Assert.AreEqual(110, matched.Fills[0].Price);
        Assert.AreEqual(3, matched.Fills[0].Quantity);
        Assert.AreEqual(true, matched.Fills[0].IsResting);
        Assert.AreEqual(Sec, matched.Fills[0].Security);
        Assert.AreEqual(Now2, matched.Fills[0].Time);
        Assert.AreEqual(CompanyId1, matched.Fills[0].CompanyId);
        Assert.AreEqual(OrderId1, matched.Fills[0].ClientOrderId);
        Assert.AreEqual(true, matched.Fills[0].IsResting);
        Assert.AreEqual(CompanyId1, matched.Fills[0].Order.CompanyId);
        Assert.AreEqual(OrderId1, matched.Fills[0].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched.Fills[0].Order.Security);
        Assert.AreEqual(Now1, matched.Fills[0].Order.CreatedTime);
        Assert.AreEqual(Now1, matched.Fills[0].Order.ModifiedTime);
        Assert.IsNull(matched.Fills[0].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Working, matched.Fills[0].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched.Fills[0].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched.Fills[0].Order.OrderValidity);
        Assert.AreEqual(Side.Buy, matched.Fills[0].Order.Side);
        Assert.AreEqual(110, matched.Fills[0].Order.Price);
        Assert.IsNull(matched.Fills[0].Order.TriggerPrice);
        Assert.AreEqual(5, matched.Fills[0].Order.Quantity);
        Assert.AreEqual(3, matched.Fills[0].Order.FilledQuantity);
        Assert.AreEqual(2, matched.Fills[0].Order.RemainingQuantity);

        Assert.AreEqual(Sec, matched.Fills[1].Security);
        Assert.AreEqual(Now2, matched.Fills[1].Time);
        Assert.AreEqual(CompanyId2, matched.Fills[1].CompanyId);
        Assert.AreEqual(OrderId2, matched.Fills[1].ClientOrderId);
        Assert.AreEqual(110, matched.Fills[1].Price);
        Assert.AreEqual(3, matched.Fills[1].Quantity);
        Assert.AreEqual(false, matched.Fills[1].IsResting);
        Assert.AreEqual(CompanyId2, matched.Fills[1].Order.CompanyId);
        Assert.AreEqual(OrderId2, matched.Fills[1].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched.Fills[1].Order.Security);
        Assert.AreEqual(Now2, matched.Fills[1].Order.CreatedTime);
        Assert.AreEqual(Now2, matched.Fills[1].Order.ModifiedTime);
        Assert.AreEqual(Now2, matched.Fills[1].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Filled, matched.Fills[1].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched.Fills[1].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched.Fills[1].Order.OrderValidity);
        Assert.AreEqual(Side.Sell, matched.Fills[1].Order.Side);
        Assert.AreEqual(100, matched.Fills[1].Order.Price);
        Assert.IsNull(matched.Fills[1].Order.TriggerPrice);
        Assert.AreEqual(3, matched.Fills[1].Order.Quantity);
        Assert.AreEqual(3, matched.Fills[1].Order.FilledQuantity);
        Assert.AreEqual(0, matched.Fills[1].Order.RemainingQuantity);
    }

    [Test]
    public void LimitOrder_MatchSellAgainstOrderByTime()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 5, 110);
        TimeProvider.SetCurrentTime(Now2);
        Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Buy, 5, 120);
        TimeProvider.SetCurrentTime(Now3);

        // act
        var events = Book.CreateLimitOrder(CompanyId3, OrderId3, new OrderValidity.Day(), Side.Sell, 3, 100);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(2, events.Count);

        var matched = events[1] as OrdersMatched;
        Assert.IsNotNull(matched);
        Assert.AreEqual(Sec, matched.Security);
        Assert.AreEqual(Now3, matched.Time);
        Assert.AreEqual(120, matched.Price);
        Assert.AreEqual(3, matched.Quantity);
        Assert.IsNotNull(matched.Fills);
        Assert.AreEqual(2, matched.Fills.Count);

        Assert.AreEqual(Sec, matched.Fills[0].Security);
        Assert.AreEqual(Now3, matched.Fills[0].Time);
        Assert.AreEqual(CompanyId2, matched.Fills[0].CompanyId);
        Assert.AreEqual(OrderId2, matched.Fills[0].ClientOrderId);
        Assert.AreEqual(120, matched.Fills[0].Price);
        Assert.AreEqual(3, matched.Fills[0].Quantity);
        Assert.AreEqual(true, matched.Fills[0].IsResting);
        Assert.AreEqual(CompanyId2, matched.Fills[0].Order.CompanyId);
        Assert.AreEqual(OrderId2, matched.Fills[0].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched.Fills[0].Order.Security);
        Assert.AreEqual(Now2, matched.Fills[0].Order.CreatedTime);
        Assert.AreEqual(Now2, matched.Fills[0].Order.ModifiedTime);
        Assert.IsNull(matched.Fills[0].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Working, matched.Fills[0].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched.Fills[0].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched.Fills[0].Order.OrderValidity);
        Assert.AreEqual(Side.Buy, matched.Fills[0].Order.Side);
        Assert.AreEqual(120, matched.Fills[0].Order.Price);
        Assert.IsNull(matched.Fills[0].Order.TriggerPrice);
        Assert.AreEqual(5, matched.Fills[0].Order.Quantity);
        Assert.AreEqual(3, matched.Fills[0].Order.FilledQuantity);
        Assert.AreEqual(2, matched.Fills[0].Order.RemainingQuantity);

        Assert.AreEqual(Sec, matched.Fills[1].Security);
        Assert.AreEqual(Now3, matched.Fills[1].Time);
        Assert.AreEqual(CompanyId3, matched.Fills[1].CompanyId);
        Assert.AreEqual(OrderId3, matched.Fills[1].ClientOrderId);
        Assert.AreEqual(120, matched.Fills[1].Price);
        Assert.AreEqual(3, matched.Fills[1].Quantity);
        Assert.AreEqual(false, matched.Fills[1].IsResting);
        Assert.AreEqual(CompanyId3, matched.Fills[1].Order.CompanyId);
        Assert.AreEqual(OrderId3, matched.Fills[1].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched.Fills[1].Order.Security);
        Assert.AreEqual(Now3, matched.Fills[1].Order.CreatedTime);
        Assert.AreEqual(Now3, matched.Fills[1].Order.ModifiedTime);
        Assert.AreEqual(Now3, matched.Fills[1].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Filled, matched.Fills[1].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched.Fills[1].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched.Fills[1].Order.OrderValidity);
        Assert.AreEqual(Side.Sell, matched.Fills[1].Order.Side);
        Assert.AreEqual(100, matched.Fills[1].Order.Price);
        Assert.IsNull(matched.Fills[1].Order.TriggerPrice);
        Assert.AreEqual(3, matched.Fills[1].Order.Quantity);
        Assert.AreEqual(3, matched.Fills[1].Order.FilledQuantity);
        Assert.AreEqual(0, matched.Fills[1].Order.RemainingQuantity);
    }

    [Test]
    public void LimitOrder_MatchSellAgainstOrdersByPrice()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 5, 110);
        TimeProvider.SetCurrentTime(Now2);
        Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Buy, 5, 120);
        TimeProvider.SetCurrentTime(Now3);

        // act
        var events = Book.CreateLimitOrder(CompanyId3, OrderId3, new OrderValidity.Day(), Side.Sell, 8, 100);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(3, events.Count);

        var matched1 = events[1] as OrdersMatched;
        Assert.IsNotNull(matched1);
        Assert.AreEqual(Sec, matched1.Security);
        Assert.AreEqual(Now3, matched1.Time);
        Assert.AreEqual(120, matched1.Price);
        Assert.AreEqual(5, matched1.Quantity);
        Assert.IsNotNull(matched1.Fills);
        Assert.AreEqual(2, matched1.Fills.Count);

        Assert.AreEqual(Sec, matched1.Fills[0].Security);
        Assert.AreEqual(Now3, matched1.Fills[0].Time);
        Assert.AreEqual(CompanyId2, matched1.Fills[0].CompanyId);
        Assert.AreEqual(OrderId2, matched1.Fills[0].ClientOrderId);
        Assert.AreEqual(120, matched1.Fills[0].Price);
        Assert.AreEqual(5, matched1.Fills[0].Quantity);
        Assert.AreEqual(true, matched1.Fills[0].IsResting);
        Assert.AreEqual(CompanyId2, matched1.Fills[0].Order.CompanyId);
        Assert.AreEqual(OrderId2, matched1.Fills[0].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched1.Fills[0].Order.Security);
        Assert.AreEqual(Now2, matched1.Fills[0].Order.CreatedTime);
        Assert.AreEqual(Now2, matched1.Fills[0].Order.ModifiedTime);
        Assert.AreEqual(Now3, matched1.Fills[0].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Filled, matched1.Fills[0].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched1.Fills[0].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched1.Fills[0].Order.OrderValidity);
        Assert.AreEqual(Side.Buy, matched1.Fills[0].Order.Side);
        Assert.AreEqual(120, matched1.Fills[0].Order.Price);
        Assert.IsNull(matched1.Fills[0].Order.TriggerPrice);
        Assert.AreEqual(5, matched1.Fills[0].Order.Quantity);
        Assert.AreEqual(5, matched1.Fills[0].Order.FilledQuantity);
        Assert.AreEqual(0, matched1.Fills[0].Order.RemainingQuantity);

        Assert.AreEqual(Sec, matched1.Fills[1].Security);
        Assert.AreEqual(Now3, matched1.Fills[1].Time);
        Assert.AreEqual(CompanyId3, matched1.Fills[1].CompanyId);
        Assert.AreEqual(OrderId3, matched1.Fills[1].ClientOrderId);
        Assert.AreEqual(120, matched1.Fills[1].Price);
        Assert.AreEqual(5, matched1.Fills[1].Quantity);
        Assert.AreEqual(false, matched1.Fills[1].IsResting);
        Assert.AreEqual(CompanyId3, matched1.Fills[1].Order.CompanyId);
        Assert.AreEqual(OrderId3, matched1.Fills[1].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched1.Fills[1].Order.Security);
        Assert.AreEqual(Now3, matched1.Fills[1].Order.CreatedTime);
        Assert.AreEqual(Now3, matched1.Fills[1].Order.ModifiedTime);
        Assert.IsNull(matched1.Fills[1].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Working, matched1.Fills[1].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched1.Fills[1].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched1.Fills[1].Order.OrderValidity);
        Assert.AreEqual(Side.Sell, matched1.Fills[1].Order.Side);
        Assert.AreEqual(100, matched1.Fills[1].Order.Price);
        Assert.IsNull(matched1.Fills[1].Order.TriggerPrice);
        Assert.AreEqual(8, matched1.Fills[1].Order.Quantity);
        Assert.AreEqual(5, matched1.Fills[1].Order.FilledQuantity);
        Assert.AreEqual(3, matched1.Fills[1].Order.RemainingQuantity);

        var matched2 = events[2] as OrdersMatched;
        Assert.IsNotNull(matched2);
        Assert.AreEqual(Sec, matched2.Security);
        Assert.AreEqual(Now3, matched2.Time);
        Assert.AreEqual(110, matched2.Price);
        Assert.AreEqual(3, matched2.Quantity);
        Assert.IsNotNull(matched2.Fills);
        Assert.AreEqual(2, matched2.Fills.Count);

        Assert.AreEqual(Sec, matched2.Fills[0].Security);
        Assert.AreEqual(Now3, matched2.Fills[0].Time);
        Assert.AreEqual(CompanyId1, matched2.Fills[0].CompanyId);
        Assert.AreEqual(OrderId1, matched2.Fills[0].ClientOrderId);
        Assert.AreEqual(110, matched2.Fills[0].Price);
        Assert.AreEqual(3, matched2.Fills[0].Quantity);
        Assert.AreEqual(true, matched2.Fills[0].IsResting);
        Assert.AreEqual(CompanyId1, matched2.Fills[0].Order.CompanyId);
        Assert.AreEqual(OrderId1, matched2.Fills[0].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched2.Fills[0].Order.Security);
        Assert.AreEqual(Now1, matched2.Fills[0].Order.CreatedTime);
        Assert.AreEqual(Now1, matched2.Fills[0].Order.ModifiedTime);
        Assert.IsNull(matched2.Fills[0].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Working, matched2.Fills[0].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched2.Fills[0].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched2.Fills[0].Order.OrderValidity);
        Assert.AreEqual(Side.Buy, matched2.Fills[0].Order.Side);
        Assert.AreEqual(110, matched2.Fills[0].Order.Price);
        Assert.IsNull(matched2.Fills[0].Order.TriggerPrice);
        Assert.AreEqual(5, matched2.Fills[0].Order.Quantity);
        Assert.AreEqual(3, matched2.Fills[0].Order.FilledQuantity);
        Assert.AreEqual(2, matched2.Fills[0].Order.RemainingQuantity);

        Assert.AreEqual(Sec, matched2.Fills[1].Security);
        Assert.AreEqual(Now3, matched2.Fills[1].Time);
        Assert.AreEqual(CompanyId3, matched2.Fills[1].CompanyId);
        Assert.AreEqual(OrderId3, matched2.Fills[1].ClientOrderId);
        Assert.AreEqual(110, matched2.Fills[1].Price);
        Assert.AreEqual(3, matched2.Fills[1].Quantity);
        Assert.AreEqual(false, matched2.Fills[1].IsResting);
        Assert.AreEqual(CompanyId3, matched2.Fills[1].Order.CompanyId);
        Assert.AreEqual(OrderId3, matched2.Fills[1].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched2.Fills[1].Order.Security);
        Assert.AreEqual(Now3, matched2.Fills[1].Order.CreatedTime);
        Assert.AreEqual(Now3, matched2.Fills[1].Order.ModifiedTime);
        Assert.AreEqual(Now3, matched2.Fills[1].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Filled, matched2.Fills[1].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched2.Fills[1].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched2.Fills[1].Order.OrderValidity);
        Assert.AreEqual(Side.Sell, matched2.Fills[1].Order.Side);
        Assert.AreEqual(100, matched2.Fills[1].Order.Price);
        Assert.IsNull(matched2.Fills[1].Order.TriggerPrice);
        Assert.AreEqual(8, matched2.Fills[1].Order.Quantity);
        Assert.AreEqual(8, matched2.Fills[1].Order.FilledQuantity);
        Assert.AreEqual(0, matched2.Fills[1].Order.RemainingQuantity);
    }

    [Test]
    public void LimitOrder_MatchSellAgainstOrderAtSamePriceByTime()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 5, 110);
        TimeProvider.SetCurrentTime(Now2);
        Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Buy, 5, 110);
        TimeProvider.SetCurrentTime(Now3);

        // act
        var events = Book.CreateLimitOrder(CompanyId3, OrderId3, new OrderValidity.Day(), Side.Sell, 3, 100);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(2, events.Count);

        var matched = events[1] as OrdersMatched;
        Assert.IsNotNull(matched);
        Assert.AreEqual(Sec, matched.Security);
        Assert.AreEqual(Now3, matched.Time);
        Assert.AreEqual(110, matched.Price);
        Assert.AreEqual(3, matched.Quantity);
        Assert.IsNotNull(matched.Fills);
        Assert.AreEqual(2, matched.Fills.Count);

        Assert.AreEqual(Sec, matched.Fills[0].Security);
        Assert.AreEqual(Now3, matched.Fills[0].Time);
        Assert.AreEqual(CompanyId1, matched.Fills[0].CompanyId);
        Assert.AreEqual(OrderId1, matched.Fills[0].ClientOrderId);
        Assert.AreEqual(110, matched.Fills[0].Price);
        Assert.AreEqual(3, matched.Fills[0].Quantity);
        Assert.AreEqual(true, matched.Fills[0].IsResting);
        Assert.AreEqual(Sec, matched.Fills[0].Security);
        Assert.AreEqual(Now3, matched.Fills[0].Time);
        Assert.AreEqual(CompanyId1, matched.Fills[0].CompanyId);
        Assert.AreEqual(OrderId1, matched.Fills[0].ClientOrderId);
        Assert.AreEqual(true, matched.Fills[0].IsResting);
        Assert.AreEqual(CompanyId1, matched.Fills[0].Order.CompanyId);
        Assert.AreEqual(OrderId1, matched.Fills[0].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched.Fills[0].Order.Security);
        Assert.AreEqual(Now1, matched.Fills[0].Order.CreatedTime);
        Assert.AreEqual(Now1, matched.Fills[0].Order.ModifiedTime);
        Assert.IsNull(matched.Fills[0].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Working, matched.Fills[0].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched.Fills[0].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched.Fills[0].Order.OrderValidity);
        Assert.AreEqual(Side.Buy, matched.Fills[0].Order.Side);
        Assert.AreEqual(110, matched.Fills[0].Order.Price);
        Assert.IsNull(matched.Fills[0].Order.TriggerPrice);
        Assert.AreEqual(5, matched.Fills[0].Order.Quantity);
        Assert.AreEqual(3, matched.Fills[0].Order.FilledQuantity);
        Assert.AreEqual(2, matched.Fills[0].Order.RemainingQuantity);

        Assert.AreEqual(Sec, matched.Fills[1].Security);
        Assert.AreEqual(Now3, matched.Fills[1].Time);
        Assert.AreEqual(CompanyId3, matched.Fills[1].CompanyId);
        Assert.AreEqual(OrderId3, matched.Fills[1].ClientOrderId);
        Assert.AreEqual(110, matched.Fills[1].Price);
        Assert.AreEqual(3, matched.Fills[1].Quantity);
        Assert.AreEqual(false, matched.Fills[1].IsResting);
        Assert.AreEqual(CompanyId3, matched.Fills[1].Order.CompanyId);
        Assert.AreEqual(OrderId3, matched.Fills[1].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched.Fills[1].Order.Security);
        Assert.AreEqual(Now3, matched.Fills[1].Order.CreatedTime);
        Assert.AreEqual(Now3, matched.Fills[1].Order.ModifiedTime);
        Assert.AreEqual(Now3, matched.Fills[1].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Filled, matched.Fills[1].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched.Fills[1].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched.Fills[1].Order.OrderValidity);
        Assert.AreEqual(Side.Sell, matched.Fills[1].Order.Side);
        Assert.AreEqual(100, matched.Fills[1].Order.Price);
        Assert.IsNull(matched.Fills[1].Order.TriggerPrice);
        Assert.AreEqual(3, matched.Fills[1].Order.Quantity);
        Assert.AreEqual(3, matched.Fills[1].Order.FilledQuantity);
        Assert.AreEqual(0, matched.Fills[1].Order.RemainingQuantity);
    }

    [Test]
    public void LimitOrder_MatchSellAgainstOrdersAtSamePriceByTime()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 5, 110);
        TimeProvider.SetCurrentTime(Now2);
        Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Buy, 5, 110);
        TimeProvider.SetCurrentTime(Now3);

        // act
        var events = Book.CreateLimitOrder(CompanyId3, OrderId3, new OrderValidity.Day(), Side.Sell, 8, 100);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(3, events.Count);

        var matched1 = events[1] as OrdersMatched;
        Assert.IsNotNull(matched1);
        Assert.AreEqual(Sec, matched1.Security);
        Assert.AreEqual(Now3, matched1.Time);
        Assert.AreEqual(110, matched1.Price);
        Assert.AreEqual(5, matched1.Quantity);
        Assert.IsNotNull(matched1.Fills);
        Assert.AreEqual(2, matched1.Fills.Count);

        Assert.AreEqual(Sec, matched1.Fills[0].Security);
        Assert.AreEqual(Now3, matched1.Fills[0].Time);
        Assert.AreEqual(CompanyId1, matched1.Fills[0].CompanyId);
        Assert.AreEqual(OrderId1, matched1.Fills[0].ClientOrderId);
        Assert.AreEqual(110, matched1.Fills[0].Price);
        Assert.AreEqual(5, matched1.Fills[0].Quantity);
        Assert.AreEqual(true, matched1.Fills[0].IsResting);
        Assert.AreEqual(CompanyId1, matched1.Fills[0].Order.CompanyId);
        Assert.AreEqual(OrderId1, matched1.Fills[0].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched1.Fills[0].Order.Security);
        Assert.AreEqual(Now1, matched1.Fills[0].Order.CreatedTime);
        Assert.AreEqual(Now1, matched1.Fills[0].Order.ModifiedTime);
        Assert.AreEqual(Now3, matched1.Fills[0].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Filled, matched1.Fills[0].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched1.Fills[0].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched1.Fills[0].Order.OrderValidity);
        Assert.AreEqual(Side.Buy, matched1.Fills[0].Order.Side);
        Assert.AreEqual(110, matched1.Fills[0].Order.Price);
        Assert.IsNull(matched1.Fills[0].Order.TriggerPrice);
        Assert.AreEqual(5, matched1.Fills[0].Order.Quantity);
        Assert.AreEqual(5, matched1.Fills[0].Order.FilledQuantity);
        Assert.AreEqual(0, matched1.Fills[0].Order.RemainingQuantity);

        Assert.AreEqual(Sec, matched1.Fills[1].Security);
        Assert.AreEqual(Now3, matched1.Fills[1].Time);
        Assert.AreEqual(CompanyId3, matched1.Fills[1].CompanyId);
        Assert.AreEqual(OrderId3, matched1.Fills[1].ClientOrderId);
        Assert.AreEqual(110, matched1.Fills[1].Price);
        Assert.AreEqual(5, matched1.Fills[1].Quantity);
        Assert.AreEqual(false, matched1.Fills[1].IsResting);
        Assert.AreEqual(CompanyId3, matched1.Fills[1].Order.CompanyId);
        Assert.AreEqual(OrderId3, matched1.Fills[1].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched1.Fills[1].Order.Security);
        Assert.AreEqual(Now3, matched1.Fills[1].Order.CreatedTime);
        Assert.AreEqual(Now3, matched1.Fills[1].Order.ModifiedTime);
        Assert.IsNull(matched1.Fills[1].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Working, matched1.Fills[1].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched1.Fills[1].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched1.Fills[1].Order.OrderValidity);
        Assert.AreEqual(Side.Sell, matched1.Fills[1].Order.Side);
        Assert.AreEqual(100, matched1.Fills[1].Order.Price);
        Assert.IsNull(matched1.Fills[1].Order.TriggerPrice);
        Assert.AreEqual(8, matched1.Fills[1].Order.Quantity);
        Assert.AreEqual(5, matched1.Fills[1].Order.FilledQuantity);
        Assert.AreEqual(3, matched1.Fills[1].Order.RemainingQuantity);

        var matched2 = events[2] as OrdersMatched;
        Assert.IsNotNull(matched2);
        Assert.AreEqual(Sec, matched2.Security);
        Assert.AreEqual(Now3, matched2.Time);
        Assert.AreEqual(110, matched2.Price);
        Assert.AreEqual(3, matched2.Quantity);
        Assert.IsNotNull(matched2.Fills);
        Assert.AreEqual(2, matched2.Fills.Count);

        Assert.AreEqual(Sec, matched2.Fills[0].Security);
        Assert.AreEqual(Now3, matched2.Fills[0].Time);
        Assert.AreEqual(CompanyId2, matched2.Fills[0].CompanyId);
        Assert.AreEqual(OrderId2, matched2.Fills[0].ClientOrderId);
        Assert.AreEqual(110, matched2.Fills[0].Price);
        Assert.AreEqual(3, matched2.Fills[0].Quantity);
        Assert.AreEqual(true, matched2.Fills[0].IsResting);
        Assert.AreEqual(CompanyId2, matched2.Fills[0].Order.CompanyId);
        Assert.AreEqual(OrderId2, matched2.Fills[0].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched2.Fills[0].Order.Security);
        Assert.AreEqual(Now2, matched2.Fills[0].Order.CreatedTime);
        Assert.AreEqual(Now2, matched2.Fills[0].Order.ModifiedTime);
        Assert.IsNull(matched2.Fills[0].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Working, matched2.Fills[0].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched2.Fills[0].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched2.Fills[0].Order.OrderValidity);
        Assert.AreEqual(Side.Buy, matched2.Fills[0].Order.Side);
        Assert.AreEqual(110, matched2.Fills[0].Order.Price);
        Assert.IsNull(matched2.Fills[0].Order.TriggerPrice);
        Assert.AreEqual(5, matched2.Fills[0].Order.Quantity);
        Assert.AreEqual(3, matched2.Fills[0].Order.FilledQuantity);
        Assert.AreEqual(2, matched2.Fills[0].Order.RemainingQuantity);

        Assert.AreEqual(Sec, matched2.Fills[1].Security);
        Assert.AreEqual(Now3, matched2.Fills[1].Time);
        Assert.AreEqual(CompanyId3, matched2.Fills[1].CompanyId);
        Assert.AreEqual(OrderId3, matched2.Fills[1].ClientOrderId);
        Assert.AreEqual(110, matched2.Fills[1].Price);
        Assert.AreEqual(3, matched2.Fills[1].Quantity);
        Assert.AreEqual(false, matched2.Fills[1].IsResting);
        Assert.AreEqual(CompanyId3, matched2.Fills[1].Order.CompanyId);
        Assert.AreEqual(OrderId3, matched2.Fills[1].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched2.Fills[1].Order.Security);
        Assert.AreEqual(Now3, matched2.Fills[1].Order.CreatedTime);
        Assert.AreEqual(Now3, matched2.Fills[1].Order.ModifiedTime);
        Assert.AreEqual(Now3, matched2.Fills[1].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Filled, matched2.Fills[1].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched2.Fills[1].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched2.Fills[1].Order.OrderValidity);
        Assert.AreEqual(Side.Sell, matched2.Fills[1].Order.Side);
        Assert.AreEqual(100, matched2.Fills[1].Order.Price);
        Assert.IsNull(matched2.Fills[1].Order.TriggerPrice);
        Assert.AreEqual(8, matched2.Fills[1].Order.Quantity);
        Assert.AreEqual(8, matched2.Fills[1].Order.FilledQuantity);
        Assert.AreEqual(0, matched2.Fills[1].Order.RemainingQuantity);
    }

    [Test]
    public void LimitOrder_MatchBuyAgainstOrderByTime()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 5, 90);
        TimeProvider.SetCurrentTime(Now2);
        Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Sell, 5, 80);
        TimeProvider.SetCurrentTime(Now3);

        // act
        var events = Book.CreateLimitOrder(CompanyId3, OrderId3, new OrderValidity.Day(), Side.Buy, 3, 100);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(2, events.Count);

        var matched = events[1] as OrdersMatched;
        Assert.IsNotNull(matched);
        Assert.AreEqual(Sec, matched.Security);
        Assert.AreEqual(Now3, matched.Time);
        Assert.AreEqual(80, matched.Price);
        Assert.AreEqual(3, matched.Quantity);
        Assert.IsNotNull(matched.Fills);
        Assert.AreEqual(2, matched.Fills.Count);

        Assert.AreEqual(Sec, matched.Fills[0].Security);
        Assert.AreEqual(Now3, matched.Fills[0].Time);
        Assert.AreEqual(CompanyId2, matched.Fills[0].CompanyId);
        Assert.AreEqual(OrderId2, matched.Fills[0].ClientOrderId);
        Assert.AreEqual(80, matched.Fills[0].Price);
        Assert.AreEqual(3, matched.Fills[0].Quantity);
        Assert.AreEqual(true, matched.Fills[0].IsResting);
        Assert.AreEqual(CompanyId2, matched.Fills[0].Order.CompanyId);
        Assert.AreEqual(OrderId2, matched.Fills[0].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched.Fills[0].Order.Security);
        Assert.AreEqual(Now2, matched.Fills[0].Order.CreatedTime);
        Assert.AreEqual(Now2, matched.Fills[0].Order.ModifiedTime);
        Assert.IsNull(matched.Fills[0].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Working, matched.Fills[0].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched.Fills[0].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched.Fills[0].Order.OrderValidity);
        Assert.AreEqual(Side.Sell, matched.Fills[0].Order.Side);
        Assert.AreEqual(80, matched.Fills[0].Order.Price);
        Assert.IsNull(matched.Fills[0].Order.TriggerPrice);
        Assert.AreEqual(5, matched.Fills[0].Order.Quantity);
        Assert.AreEqual(3, matched.Fills[0].Order.FilledQuantity);
        Assert.AreEqual(2, matched.Fills[0].Order.RemainingQuantity);

        Assert.AreEqual(Sec, matched.Fills[1].Security);
        Assert.AreEqual(Now3, matched.Fills[1].Time);
        Assert.AreEqual(CompanyId3, matched.Fills[1].CompanyId);
        Assert.AreEqual(OrderId3, matched.Fills[1].ClientOrderId);
        Assert.AreEqual(80, matched.Fills[1].Price);
        Assert.AreEqual(3, matched.Fills[1].Quantity);
        Assert.AreEqual(false, matched.Fills[1].IsResting);
        Assert.AreEqual(CompanyId3, matched.Fills[1].Order.CompanyId);
        Assert.AreEqual(OrderId3, matched.Fills[1].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched.Fills[1].Order.Security);
        Assert.AreEqual(Now3, matched.Fills[1].Order.CreatedTime);
        Assert.AreEqual(Now3, matched.Fills[1].Order.ModifiedTime);
        Assert.AreEqual(Now3, matched.Fills[1].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Filled, matched.Fills[1].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched.Fills[1].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched.Fills[1].Order.OrderValidity);
        Assert.AreEqual(Side.Buy, matched.Fills[1].Order.Side);
        Assert.AreEqual(100, matched.Fills[1].Order.Price);
        Assert.IsNull(matched.Fills[1].Order.TriggerPrice);
        Assert.AreEqual(3, matched.Fills[1].Order.Quantity);
        Assert.AreEqual(3, matched.Fills[1].Order.FilledQuantity);
        Assert.AreEqual(0, matched.Fills[1].Order.RemainingQuantity);
    }

    [Test]
    public void LimitOrder_MatchBuyAgainstOrdersByPrice()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 5, 90);
        TimeProvider.SetCurrentTime(Now2);
        Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Sell, 5, 80);
        TimeProvider.SetCurrentTime(Now3);

        // act
        var events = Book.CreateLimitOrder(CompanyId3, OrderId3, new OrderValidity.Day(), Side.Buy, 8, 100);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(3, events.Count);

        var matched1 = events[1] as OrdersMatched;
        Assert.IsNotNull(matched1);
        Assert.AreEqual(Sec, matched1.Security);
        Assert.AreEqual(Now3, matched1.Time);
        Assert.AreEqual(80, matched1.Price);
        Assert.AreEqual(5, matched1.Quantity);
        Assert.IsNotNull(matched1.Fills);
        Assert.AreEqual(2, matched1.Fills.Count);

        Assert.AreEqual(Sec, matched1.Fills[0].Security);
        Assert.AreEqual(Now3, matched1.Fills[0].Time);
        Assert.AreEqual(CompanyId2, matched1.Fills[0].CompanyId);
        Assert.AreEqual(OrderId2, matched1.Fills[0].ClientOrderId);
        Assert.AreEqual(80, matched1.Fills[0].Price);
        Assert.AreEqual(5, matched1.Fills[0].Quantity);
        Assert.AreEqual(true, matched1.Fills[0].IsResting);
        Assert.AreEqual(CompanyId2, matched1.Fills[0].Order.CompanyId);
        Assert.AreEqual(OrderId2, matched1.Fills[0].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched1.Fills[0].Order.Security);
        Assert.AreEqual(Now2, matched1.Fills[0].Order.CreatedTime);
        Assert.AreEqual(Now2, matched1.Fills[0].Order.ModifiedTime);
        Assert.AreEqual(Now3, matched1.Fills[0].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Filled, matched1.Fills[0].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched1.Fills[0].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched1.Fills[0].Order.OrderValidity);
        Assert.AreEqual(Side.Sell, matched1.Fills[0].Order.Side);
        Assert.AreEqual(80, matched1.Fills[0].Order.Price);
        Assert.IsNull(matched1.Fills[0].Order.TriggerPrice);
        Assert.AreEqual(5, matched1.Fills[0].Order.Quantity);
        Assert.AreEqual(5, matched1.Fills[0].Order.FilledQuantity);
        Assert.AreEqual(0, matched1.Fills[0].Order.RemainingQuantity);

        Assert.AreEqual(Sec, matched1.Fills[1].Security);
        Assert.AreEqual(Now3, matched1.Fills[1].Time);
        Assert.AreEqual(CompanyId3, matched1.Fills[1].CompanyId);
        Assert.AreEqual(OrderId3, matched1.Fills[1].ClientOrderId);
        Assert.AreEqual(80, matched1.Fills[1].Price);
        Assert.AreEqual(5, matched1.Fills[1].Quantity);
        Assert.AreEqual(false, matched1.Fills[1].IsResting);
        Assert.AreEqual(CompanyId3, matched1.Fills[1].Order.CompanyId);
        Assert.AreEqual(OrderId3, matched1.Fills[1].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched1.Fills[1].Order.Security);
        Assert.AreEqual(Now3, matched1.Fills[1].Order.CreatedTime);
        Assert.AreEqual(Now3, matched1.Fills[1].Order.ModifiedTime);
        Assert.IsNull(matched1.Fills[1].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Working, matched1.Fills[1].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched1.Fills[1].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched1.Fills[1].Order.OrderValidity);
        Assert.AreEqual(Side.Buy, matched1.Fills[1].Order.Side);
        Assert.AreEqual(100, matched1.Fills[1].Order.Price);
        Assert.IsNull(matched1.Fills[1].Order.TriggerPrice);
        Assert.AreEqual(8, matched1.Fills[1].Order.Quantity);
        Assert.AreEqual(5, matched1.Fills[1].Order.FilledQuantity);
        Assert.AreEqual(3, matched1.Fills[1].Order.RemainingQuantity);

        var matched2 = events[2] as OrdersMatched;
        Assert.IsNotNull(matched2);
        Assert.AreEqual(Sec, matched2.Security);
        Assert.AreEqual(Now3, matched2.Time);
        Assert.AreEqual(90, matched2.Price);
        Assert.AreEqual(3, matched2.Quantity);
        Assert.IsNotNull(matched2.Fills);
        Assert.AreEqual(2, matched2.Fills.Count);

        Assert.AreEqual(Sec, matched2.Fills[0].Security);
        Assert.AreEqual(Now3, matched2.Fills[0].Time);
        Assert.AreEqual(CompanyId1, matched2.Fills[0].CompanyId);
        Assert.AreEqual(OrderId1, matched2.Fills[0].ClientOrderId);
        Assert.AreEqual(90, matched2.Fills[0].Price);
        Assert.AreEqual(3, matched2.Fills[0].Quantity);
        Assert.AreEqual(true, matched2.Fills[0].IsResting);
        Assert.AreEqual(CompanyId1, matched2.Fills[0].Order.CompanyId);
        Assert.AreEqual(OrderId1, matched2.Fills[0].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched2.Fills[0].Order.Security);
        Assert.AreEqual(Now1, matched2.Fills[0].Order.CreatedTime);
        Assert.AreEqual(Now1, matched2.Fills[0].Order.ModifiedTime);
        Assert.IsNull(matched2.Fills[0].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Working, matched2.Fills[0].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched2.Fills[0].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched2.Fills[0].Order.OrderValidity);
        Assert.AreEqual(Side.Sell, matched2.Fills[0].Order.Side);
        Assert.AreEqual(90, matched2.Fills[0].Order.Price);
        Assert.IsNull(matched2.Fills[0].Order.TriggerPrice);
        Assert.AreEqual(5, matched2.Fills[0].Order.Quantity);
        Assert.AreEqual(3, matched2.Fills[0].Order.FilledQuantity);
        Assert.AreEqual(2, matched2.Fills[0].Order.RemainingQuantity);

        Assert.AreEqual(Sec, matched2.Fills[1].Security);
        Assert.AreEqual(Now3, matched2.Fills[1].Time);
        Assert.AreEqual(CompanyId3, matched2.Fills[1].CompanyId);
        Assert.AreEqual(OrderId3, matched2.Fills[1].ClientOrderId);
        Assert.AreEqual(90, matched2.Fills[1].Price);
        Assert.AreEqual(3, matched2.Fills[1].Quantity);
        Assert.AreEqual(false, matched2.Fills[1].IsResting);
        Assert.AreEqual(CompanyId3, matched2.Fills[1].Order.CompanyId);
        Assert.AreEqual(OrderId3, matched2.Fills[1].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched2.Fills[1].Order.Security);
        Assert.AreEqual(Now3, matched2.Fills[1].Order.CreatedTime);
        Assert.AreEqual(Now3, matched2.Fills[1].Order.ModifiedTime);
        Assert.AreEqual(Now3, matched2.Fills[1].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Filled, matched2.Fills[1].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched2.Fills[1].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched2.Fills[1].Order.OrderValidity);
        Assert.AreEqual(Side.Buy, matched2.Fills[1].Order.Side);
        Assert.AreEqual(100, matched2.Fills[1].Order.Price);
        Assert.IsNull(matched2.Fills[1].Order.TriggerPrice);
        Assert.AreEqual(8, matched2.Fills[1].Order.Quantity);
        Assert.AreEqual(8, matched2.Fills[1].Order.FilledQuantity);
        Assert.AreEqual(0, matched2.Fills[1].Order.RemainingQuantity);
    }

    [Test]
    public void LimitOrder_MatchBuyAgainstOrderAtSamePriceByTime()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 5, 90);
        TimeProvider.SetCurrentTime(Now2);
        Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Sell, 5, 90);
        TimeProvider.SetCurrentTime(Now3);

        // act
        var events = Book.CreateLimitOrder(CompanyId3, OrderId3, new OrderValidity.Day(), Side.Buy, 3, 100);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(2, events.Count);

        var matched = events[1] as OrdersMatched;
        Assert.IsNotNull(matched);
        Assert.AreEqual(Sec, matched.Security);
        Assert.AreEqual(Now3, matched.Time);
        Assert.AreEqual(90, matched.Price);
        Assert.AreEqual(3, matched.Quantity);
        Assert.IsNotNull(matched.Fills);
        Assert.AreEqual(2, matched.Fills.Count);

        Assert.AreEqual(Sec, matched.Fills[0].Security);
        Assert.AreEqual(Now3, matched.Fills[0].Time);
        Assert.AreEqual(CompanyId1, matched.Fills[0].CompanyId);
        Assert.AreEqual(OrderId1, matched.Fills[0].ClientOrderId);
        Assert.AreEqual(90, matched.Fills[0].Price);
        Assert.AreEqual(3, matched.Fills[0].Quantity);
        Assert.AreEqual(true, matched.Fills[0].IsResting);
        Assert.AreEqual(Sec, matched.Fills[0].Security);
        Assert.AreEqual(Now3, matched.Fills[0].Time);
        Assert.AreEqual(CompanyId1, matched.Fills[0].CompanyId);
        Assert.AreEqual(OrderId1, matched.Fills[0].ClientOrderId);
        Assert.AreEqual(true, matched.Fills[0].IsResting);
        Assert.AreEqual(CompanyId1, matched.Fills[0].Order.CompanyId);
        Assert.AreEqual(OrderId1, matched.Fills[0].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched.Fills[0].Order.Security);
        Assert.AreEqual(Now1, matched.Fills[0].Order.CreatedTime);
        Assert.AreEqual(Now1, matched.Fills[0].Order.ModifiedTime);
        Assert.IsNull(matched.Fills[0].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Working, matched.Fills[0].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched.Fills[0].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched.Fills[0].Order.OrderValidity);
        Assert.AreEqual(Side.Sell, matched.Fills[0].Order.Side);
        Assert.AreEqual(90, matched.Fills[0].Order.Price);
        Assert.IsNull(matched.Fills[0].Order.TriggerPrice);
        Assert.AreEqual(5, matched.Fills[0].Order.Quantity);
        Assert.AreEqual(3, matched.Fills[0].Order.FilledQuantity);
        Assert.AreEqual(2, matched.Fills[0].Order.RemainingQuantity);

        Assert.AreEqual(Sec, matched.Fills[1].Security);
        Assert.AreEqual(Now3, matched.Fills[1].Time);
        Assert.AreEqual(CompanyId3, matched.Fills[1].CompanyId);
        Assert.AreEqual(OrderId3, matched.Fills[1].ClientOrderId);
        Assert.AreEqual(90, matched.Fills[1].Price);
        Assert.AreEqual(3, matched.Fills[1].Quantity);
        Assert.AreEqual(false, matched.Fills[1].IsResting);
        Assert.AreEqual(CompanyId3, matched.Fills[1].Order.CompanyId);
        Assert.AreEqual(OrderId3, matched.Fills[1].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched.Fills[1].Order.Security);
        Assert.AreEqual(Now3, matched.Fills[1].Order.CreatedTime);
        Assert.AreEqual(Now3, matched.Fills[1].Order.ModifiedTime);
        Assert.AreEqual(Now3, matched.Fills[1].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Filled, matched.Fills[1].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched.Fills[1].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched.Fills[1].Order.OrderValidity);
        Assert.AreEqual(Side.Buy, matched.Fills[1].Order.Side);
        Assert.AreEqual(100, matched.Fills[1].Order.Price);
        Assert.IsNull(matched.Fills[1].Order.TriggerPrice);
        Assert.AreEqual(3, matched.Fills[1].Order.Quantity);
        Assert.AreEqual(3, matched.Fills[1].Order.FilledQuantity);
        Assert.AreEqual(0, matched.Fills[1].Order.RemainingQuantity);
    }

    [Test]
    public void LimitOrder_MatchBuyAgainstOrdersAtSamePriceByTime()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 5, 90);
        TimeProvider.SetCurrentTime(Now2);
        Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Sell, 5, 90);
        TimeProvider.SetCurrentTime(Now3);

        // act
        var events = Book.CreateLimitOrder(CompanyId3, OrderId3, new OrderValidity.Day(), Side.Buy, 8, 100);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(3, events.Count);

        var matched1 = events[1] as OrdersMatched;
        Assert.IsNotNull(matched1);
        Assert.AreEqual(Sec, matched1.Security);
        Assert.AreEqual(Now3, matched1.Time);
        Assert.AreEqual(90, matched1.Price);
        Assert.AreEqual(5, matched1.Quantity);
        Assert.IsNotNull(matched1.Fills);
        Assert.AreEqual(2, matched1.Fills.Count);

        Assert.AreEqual(Sec, matched1.Fills[0].Security);
        Assert.AreEqual(Now3, matched1.Fills[0].Time);
        Assert.AreEqual(CompanyId1, matched1.Fills[0].CompanyId);
        Assert.AreEqual(OrderId1, matched1.Fills[0].ClientOrderId);
        Assert.AreEqual(90, matched1.Fills[0].Price);
        Assert.AreEqual(5, matched1.Fills[0].Quantity);
        Assert.AreEqual(true, matched1.Fills[0].IsResting);
        Assert.AreEqual(CompanyId1, matched1.Fills[0].Order.CompanyId);
        Assert.AreEqual(OrderId1, matched1.Fills[0].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched1.Fills[0].Order.Security);
        Assert.AreEqual(Now1, matched1.Fills[0].Order.CreatedTime);
        Assert.AreEqual(Now1, matched1.Fills[0].Order.ModifiedTime);
        Assert.AreEqual(Now3, matched1.Fills[0].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Filled, matched1.Fills[0].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched1.Fills[0].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched1.Fills[0].Order.OrderValidity);
        Assert.AreEqual(Side.Sell, matched1.Fills[0].Order.Side);
        Assert.AreEqual(90, matched1.Fills[0].Order.Price);
        Assert.IsNull(matched1.Fills[0].Order.TriggerPrice);
        Assert.AreEqual(5, matched1.Fills[0].Order.Quantity);
        Assert.AreEqual(5, matched1.Fills[0].Order.FilledQuantity);
        Assert.AreEqual(0, matched1.Fills[0].Order.RemainingQuantity);

        Assert.AreEqual(Sec, matched1.Fills[1].Security);
        Assert.AreEqual(Now3, matched1.Fills[1].Time);
        Assert.AreEqual(CompanyId3, matched1.Fills[1].CompanyId);
        Assert.AreEqual(OrderId3, matched1.Fills[1].ClientOrderId);
        Assert.AreEqual(90, matched1.Fills[1].Price);
        Assert.AreEqual(5, matched1.Fills[1].Quantity);
        Assert.AreEqual(false, matched1.Fills[1].IsResting);
        Assert.AreEqual(CompanyId3, matched1.Fills[1].Order.CompanyId);
        Assert.AreEqual(OrderId3, matched1.Fills[1].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched1.Fills[1].Order.Security);
        Assert.AreEqual(Now3, matched1.Fills[1].Order.CreatedTime);
        Assert.AreEqual(Now3, matched1.Fills[1].Order.ModifiedTime);
        Assert.IsNull(matched1.Fills[1].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Working, matched1.Fills[1].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched1.Fills[1].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched1.Fills[1].Order.OrderValidity);
        Assert.AreEqual(Side.Buy, matched1.Fills[1].Order.Side);
        Assert.AreEqual(100, matched1.Fills[1].Order.Price);
        Assert.IsNull(matched1.Fills[1].Order.TriggerPrice);
        Assert.AreEqual(8, matched1.Fills[1].Order.Quantity);
        Assert.AreEqual(5, matched1.Fills[1].Order.FilledQuantity);
        Assert.AreEqual(3, matched1.Fills[1].Order.RemainingQuantity);

        var matched2 = events[2] as OrdersMatched;
        Assert.IsNotNull(matched2);
        Assert.AreEqual(Sec, matched2.Security);
        Assert.AreEqual(Now3, matched2.Time);
        Assert.AreEqual(90, matched2.Price);
        Assert.AreEqual(3, matched2.Quantity);
        Assert.IsNotNull(matched2.Fills);
        Assert.AreEqual(2, matched2.Fills.Count);

        Assert.AreEqual(Sec, matched2.Fills[0].Security);
        Assert.AreEqual(Now3, matched2.Fills[0].Time);
        Assert.AreEqual(CompanyId2, matched2.Fills[0].CompanyId);
        Assert.AreEqual(OrderId2, matched2.Fills[0].ClientOrderId);
        Assert.AreEqual(90, matched2.Fills[0].Price);
        Assert.AreEqual(3, matched2.Fills[0].Quantity);
        Assert.AreEqual(true, matched2.Fills[0].IsResting);
        Assert.AreEqual(CompanyId2, matched2.Fills[0].Order.CompanyId);
        Assert.AreEqual(OrderId2, matched2.Fills[0].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched2.Fills[0].Order.Security);
        Assert.AreEqual(Now2, matched2.Fills[0].Order.CreatedTime);
        Assert.AreEqual(Now2, matched2.Fills[0].Order.ModifiedTime);
        Assert.IsNull(matched2.Fills[0].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Working, matched2.Fills[0].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched2.Fills[0].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched2.Fills[0].Order.OrderValidity);
        Assert.AreEqual(Side.Sell, matched2.Fills[0].Order.Side);
        Assert.AreEqual(90, matched2.Fills[0].Order.Price);
        Assert.IsNull(matched2.Fills[0].Order.TriggerPrice);
        Assert.AreEqual(5, matched2.Fills[0].Order.Quantity);
        Assert.AreEqual(3, matched2.Fills[0].Order.FilledQuantity);
        Assert.AreEqual(2, matched2.Fills[0].Order.RemainingQuantity);

        Assert.AreEqual(Sec, matched2.Fills[1].Security);
        Assert.AreEqual(Now3, matched2.Fills[1].Time);
        Assert.AreEqual(CompanyId3, matched2.Fills[1].CompanyId);
        Assert.AreEqual(OrderId3, matched2.Fills[1].ClientOrderId);
        Assert.AreEqual(90, matched2.Fills[1].Price);
        Assert.AreEqual(3, matched2.Fills[1].Quantity);
        Assert.AreEqual(false, matched2.Fills[1].IsResting);
        Assert.AreEqual(CompanyId3, matched2.Fills[1].Order.CompanyId);
        Assert.AreEqual(OrderId3, matched2.Fills[1].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched2.Fills[1].Order.Security);
        Assert.AreEqual(Now3, matched2.Fills[1].Order.CreatedTime);
        Assert.AreEqual(Now3, matched2.Fills[1].Order.ModifiedTime);
        Assert.AreEqual(Now3, matched2.Fills[1].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Filled, matched2.Fills[1].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched2.Fills[1].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched2.Fills[1].Order.OrderValidity);
        Assert.AreEqual(Side.Buy, matched2.Fills[1].Order.Side);
        Assert.AreEqual(100, matched2.Fills[1].Order.Price);
        Assert.IsNull(matched2.Fills[1].Order.TriggerPrice);
        Assert.AreEqual(8, matched2.Fills[1].Order.Quantity);
        Assert.AreEqual(8, matched2.Fills[1].Order.FilledQuantity);
        Assert.AreEqual(0, matched2.Fills[1].Order.RemainingQuantity);
    }

    [Test]
    public void LimitOrder_MatchAgainstOrderAtSamePriceByTimeAfterIncreaseQuantity()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 5, 110);
        TimeProvider.SetCurrentTime(Now2);
        Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Buy, 5, 110);
        TimeProvider.SetCurrentTime(Now3);
        Book.UpdateOrder(CompanyId1, OrderId4, OrderId1, 7);
        TimeProvider.SetCurrentTime(Now4);

        // act
        var events = Book.CreateLimitOrder(CompanyId3, OrderId3, new OrderValidity.Day(), Side.Sell, 3, 100);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(2, events.Count);

        var matched = events[1] as OrdersMatched;
        Assert.IsNotNull(matched);
        Assert.AreEqual(Sec, matched.Security);
        Assert.AreEqual(Now4, matched.Time);
        Assert.AreEqual(110, matched.Price);
        Assert.AreEqual(3, matched.Quantity);
        Assert.IsNotNull(matched.Fills);
        Assert.AreEqual(2, matched.Fills.Count);

        Assert.AreEqual(Sec, matched.Fills[0].Security);
        Assert.AreEqual(Now4, matched.Fills[0].Time);
        Assert.AreEqual(CompanyId2, matched.Fills[0].CompanyId);
        Assert.AreEqual(OrderId2, matched.Fills[0].ClientOrderId);
        Assert.AreEqual(110, matched.Fills[0].Price);
        Assert.AreEqual(3, matched.Fills[0].Quantity);
        Assert.AreEqual(true, matched.Fills[0].IsResting);
        Assert.AreEqual(CompanyId2, matched.Fills[0].Order.CompanyId);
        Assert.AreEqual(OrderId2, matched.Fills[0].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched.Fills[0].Order.Security);
        Assert.AreEqual(Now2, matched.Fills[0].Order.CreatedTime);
        Assert.AreEqual(Now2, matched.Fills[0].Order.ModifiedTime);
        Assert.IsNull(matched.Fills[0].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Working, matched.Fills[0].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched.Fills[0].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched.Fills[0].Order.OrderValidity);
        Assert.AreEqual(Side.Buy, matched.Fills[0].Order.Side);
        Assert.AreEqual(110, matched.Fills[0].Order.Price);
        Assert.IsNull(matched.Fills[0].Order.TriggerPrice);
        Assert.AreEqual(5, matched.Fills[0].Order.Quantity);
        Assert.AreEqual(3, matched.Fills[0].Order.FilledQuantity);
        Assert.AreEqual(2, matched.Fills[0].Order.RemainingQuantity);

        Assert.AreEqual(Sec, matched.Fills[1].Security);
        Assert.AreEqual(Now4, matched.Fills[1].Time);
        Assert.AreEqual(CompanyId3, matched.Fills[1].CompanyId);
        Assert.AreEqual(OrderId3, matched.Fills[1].ClientOrderId);
        Assert.AreEqual(110, matched.Fills[1].Price);
        Assert.AreEqual(3, matched.Fills[1].Quantity);
        Assert.AreEqual(false, matched.Fills[1].IsResting);
        Assert.AreEqual(CompanyId3, matched.Fills[1].Order.CompanyId);
        Assert.AreEqual(OrderId3, matched.Fills[1].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched.Fills[1].Order.Security);
        Assert.AreEqual(Now4, matched.Fills[1].Order.CreatedTime);
        Assert.AreEqual(Now4, matched.Fills[1].Order.ModifiedTime);
        Assert.AreEqual(Now4, matched.Fills[1].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Filled, matched.Fills[1].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched.Fills[1].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched.Fills[1].Order.OrderValidity);
        Assert.AreEqual(Side.Sell, matched.Fills[1].Order.Side);
        Assert.AreEqual(100, matched.Fills[1].Order.Price);
        Assert.IsNull(matched.Fills[1].Order.TriggerPrice);
        Assert.AreEqual(3, matched.Fills[1].Order.Quantity);
        Assert.AreEqual(3, matched.Fills[1].Order.FilledQuantity);
        Assert.AreEqual(0, matched.Fills[1].Order.RemainingQuantity);
    }

    [Test]
    public void LimitOrder_MatchAgainstOrdersAtSamePriceByTimeAfterIncreaseQuantity()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 5, 110);
        TimeProvider.SetCurrentTime(Now2);
        Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Buy, 5, 110);
        TimeProvider.SetCurrentTime(Now3);
        Book.UpdateOrder(CompanyId1, OrderId4, OrderId1, 6);
        TimeProvider.SetCurrentTime(Now4);

        // act
        var events = Book.CreateLimitOrder(CompanyId3, OrderId3, new OrderValidity.Day(), Side.Sell, 8, 100);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(3, events.Count);

        var matched1 = events[1] as OrdersMatched;
        Assert.IsNotNull(matched1);
        Assert.AreEqual(Sec, matched1.Security);
        Assert.AreEqual(Now4, matched1.Time);
        Assert.AreEqual(110, matched1.Price);
        Assert.AreEqual(5, matched1.Quantity);
        Assert.IsNotNull(matched1.Fills);
        Assert.AreEqual(2, matched1.Fills.Count);

        Assert.AreEqual(Sec, matched1.Fills[0].Security);
        Assert.AreEqual(Now4, matched1.Fills[0].Time);
        Assert.AreEqual(CompanyId2, matched1.Fills[0].CompanyId);
        Assert.AreEqual(OrderId2, matched1.Fills[0].ClientOrderId);
        Assert.AreEqual(110, matched1.Fills[0].Price);
        Assert.AreEqual(5, matched1.Fills[0].Quantity);
        Assert.AreEqual(true, matched1.Fills[0].IsResting);
        Assert.AreEqual(CompanyId2, matched1.Fills[0].Order.CompanyId);
        Assert.AreEqual(OrderId2, matched1.Fills[0].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched1.Fills[0].Order.Security);
        Assert.AreEqual(Now2, matched1.Fills[0].Order.CreatedTime);
        Assert.AreEqual(Now2, matched1.Fills[0].Order.ModifiedTime);
        Assert.AreEqual(Now4, matched1.Fills[0].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Filled, matched1.Fills[0].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched1.Fills[0].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched1.Fills[0].Order.OrderValidity);
        Assert.AreEqual(Side.Buy, matched1.Fills[0].Order.Side);
        Assert.AreEqual(110, matched1.Fills[0].Order.Price);
        Assert.IsNull(matched1.Fills[0].Order.TriggerPrice);
        Assert.AreEqual(5, matched1.Fills[0].Order.Quantity);
        Assert.AreEqual(5, matched1.Fills[0].Order.FilledQuantity);
        Assert.AreEqual(0, matched1.Fills[0].Order.RemainingQuantity);

        Assert.AreEqual(Sec, matched1.Fills[1].Security);
        Assert.AreEqual(Now4, matched1.Fills[1].Time);
        Assert.AreEqual(CompanyId3, matched1.Fills[1].CompanyId);
        Assert.AreEqual(OrderId3, matched1.Fills[1].ClientOrderId);
        Assert.AreEqual(110, matched1.Fills[1].Price);
        Assert.AreEqual(5, matched1.Fills[1].Quantity);
        Assert.AreEqual(false, matched1.Fills[1].IsResting);
        Assert.AreEqual(CompanyId3, matched1.Fills[1].Order.CompanyId);
        Assert.AreEqual(OrderId3, matched1.Fills[1].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched1.Fills[1].Order.Security);
        Assert.AreEqual(Now4, matched1.Fills[1].Order.CreatedTime);
        Assert.AreEqual(Now4, matched1.Fills[1].Order.ModifiedTime);
        Assert.IsNull(matched1.Fills[1].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Working, matched1.Fills[1].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched1.Fills[1].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched1.Fills[1].Order.OrderValidity);
        Assert.AreEqual(Side.Sell, matched1.Fills[1].Order.Side);
        Assert.AreEqual(100, matched1.Fills[1].Order.Price);
        Assert.IsNull(matched1.Fills[1].Order.TriggerPrice);
        Assert.AreEqual(8, matched1.Fills[1].Order.Quantity);
        Assert.AreEqual(5, matched1.Fills[1].Order.FilledQuantity);
        Assert.AreEqual(3, matched1.Fills[1].Order.RemainingQuantity);

        var matched2 = events[2] as OrdersMatched;
        Assert.IsNotNull(matched2);
        Assert.AreEqual(Sec, matched2.Security);
        Assert.AreEqual(Now4, matched2.Time);
        Assert.AreEqual(110, matched2.Price);
        Assert.AreEqual(3, matched2.Quantity);
        Assert.IsNotNull(matched2.Fills);
        Assert.AreEqual(2, matched2.Fills.Count);

        Assert.AreEqual(Sec, matched2.Fills[0].Security);
        Assert.AreEqual(Now4, matched2.Fills[0].Time);
        Assert.AreEqual(CompanyId1, matched2.Fills[0].CompanyId);
        Assert.AreEqual(OrderId4, matched2.Fills[0].ClientOrderId);
        Assert.AreEqual(110, matched2.Fills[0].Price);
        Assert.AreEqual(3, matched2.Fills[0].Quantity);
        Assert.AreEqual(true, matched2.Fills[0].IsResting);
        Assert.AreEqual(CompanyId1, matched2.Fills[0].Order.CompanyId);
        Assert.AreEqual(OrderId4, matched2.Fills[0].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched2.Fills[0].Order.Security);
        Assert.AreEqual(Now1, matched2.Fills[0].Order.CreatedTime);
        Assert.AreEqual(Now3, matched2.Fills[0].Order.ModifiedTime);
        Assert.IsNull(matched2.Fills[0].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Working, matched2.Fills[0].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched2.Fills[0].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched2.Fills[0].Order.OrderValidity);
        Assert.AreEqual(Side.Buy, matched2.Fills[0].Order.Side);
        Assert.AreEqual(110, matched2.Fills[0].Order.Price);
        Assert.IsNull(matched2.Fills[0].Order.TriggerPrice);
        Assert.AreEqual(6, matched2.Fills[0].Order.Quantity);
        Assert.AreEqual(3, matched2.Fills[0].Order.FilledQuantity);
        Assert.AreEqual(3, matched2.Fills[0].Order.RemainingQuantity);

        Assert.AreEqual(Sec, matched2.Fills[1].Security);
        Assert.AreEqual(Now4, matched2.Fills[1].Time);
        Assert.AreEqual(CompanyId3, matched2.Fills[1].CompanyId);
        Assert.AreEqual(OrderId3, matched2.Fills[1].ClientOrderId);
        Assert.AreEqual(110, matched2.Fills[1].Price);
        Assert.AreEqual(3, matched2.Fills[1].Quantity);
        Assert.AreEqual(false, matched2.Fills[1].IsResting);
        Assert.AreEqual(CompanyId3, matched2.Fills[1].Order.CompanyId);
        Assert.AreEqual(OrderId3, matched2.Fills[1].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched2.Fills[1].Order.Security);
        Assert.AreEqual(Now4, matched2.Fills[1].Order.CreatedTime);
        Assert.AreEqual(Now4, matched2.Fills[1].Order.ModifiedTime);
        Assert.AreEqual(Now4, matched2.Fills[1].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Filled, matched2.Fills[1].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched2.Fills[1].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched2.Fills[1].Order.OrderValidity);
        Assert.AreEqual(Side.Sell, matched2.Fills[1].Order.Side);
        Assert.AreEqual(100, matched2.Fills[1].Order.Price);
        Assert.IsNull(matched2.Fills[1].Order.TriggerPrice);
        Assert.AreEqual(8, matched2.Fills[1].Order.Quantity);
        Assert.AreEqual(8, matched2.Fills[1].Order.FilledQuantity);
        Assert.AreEqual(0, matched2.Fills[1].Order.RemainingQuantity);
    }

    [Test]
    public void LimitOrder_MatchAgainstOrderAtSamePriceByTimeAfterDecreaseQuantity()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 5, 110);
        TimeProvider.SetCurrentTime(Now2);
        Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Buy, 5, 110);
        TimeProvider.SetCurrentTime(Now3);
        Book.UpdateOrder(CompanyId1, OrderId4, OrderId1, 4, 110);
        TimeProvider.SetCurrentTime(Now4);

        // act
        var events = Book.CreateLimitOrder(CompanyId3, OrderId3, new OrderValidity.Day(), Side.Sell, 3, 100);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(2, events.Count);

        var matched = events[1] as OrdersMatched;
        Assert.IsNotNull(matched);
        Assert.AreEqual(Sec, matched.Security);
        Assert.AreEqual(Now4, matched.Time);
        Assert.AreEqual(110, matched.Price);
        Assert.AreEqual(3, matched.Quantity);
        Assert.IsNotNull(matched.Fills);
        Assert.AreEqual(2, matched.Fills.Count);

        Assert.AreEqual(Sec, matched.Fills[0].Security);
        Assert.AreEqual(Now4, matched.Fills[0].Time);
        Assert.AreEqual(CompanyId1, matched.Fills[0].CompanyId);
        Assert.AreEqual(OrderId4, matched.Fills[0].ClientOrderId);
        Assert.AreEqual(110, matched.Fills[0].Price);
        Assert.AreEqual(3, matched.Fills[0].Quantity);
        Assert.AreEqual(true, matched.Fills[0].IsResting);
        Assert.AreEqual(Sec, matched.Fills[0].Security);
        Assert.AreEqual(Now4, matched.Fills[0].Time);
        Assert.AreEqual(CompanyId1, matched.Fills[0].CompanyId);
        Assert.AreEqual(OrderId4, matched.Fills[0].ClientOrderId);
        Assert.AreEqual(true, matched.Fills[0].IsResting);
        Assert.AreEqual(CompanyId1, matched.Fills[0].Order.CompanyId);
        Assert.AreEqual(OrderId4, matched.Fills[0].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched.Fills[0].Order.Security);
        Assert.AreEqual(Now1, matched.Fills[0].Order.CreatedTime);
        Assert.AreEqual(Now3, matched.Fills[0].Order.ModifiedTime);
        Assert.IsNull(matched.Fills[0].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Working, matched.Fills[0].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched.Fills[0].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched.Fills[0].Order.OrderValidity);
        Assert.AreEqual(Side.Buy, matched.Fills[0].Order.Side);
        Assert.AreEqual(110, matched.Fills[0].Order.Price);
        Assert.IsNull(matched.Fills[0].Order.TriggerPrice);
        Assert.AreEqual(4, matched.Fills[0].Order.Quantity);
        Assert.AreEqual(3, matched.Fills[0].Order.FilledQuantity);
        Assert.AreEqual(1, matched.Fills[0].Order.RemainingQuantity);

        Assert.AreEqual(Sec, matched.Fills[1].Security);
        Assert.AreEqual(Now4, matched.Fills[1].Time);
        Assert.AreEqual(CompanyId3, matched.Fills[1].CompanyId);
        Assert.AreEqual(OrderId3, matched.Fills[1].ClientOrderId);
        Assert.AreEqual(110, matched.Fills[1].Price);
        Assert.AreEqual(3, matched.Fills[1].Quantity);
        Assert.AreEqual(false, matched.Fills[1].IsResting);
        Assert.AreEqual(CompanyId3, matched.Fills[1].Order.CompanyId);
        Assert.AreEqual(OrderId3, matched.Fills[1].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched.Fills[1].Order.Security);
        Assert.AreEqual(Now4, matched.Fills[1].Order.CreatedTime);
        Assert.AreEqual(Now4, matched.Fills[1].Order.ModifiedTime);
        Assert.AreEqual(Now4, matched.Fills[1].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Filled, matched.Fills[1].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched.Fills[1].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched.Fills[1].Order.OrderValidity);
        Assert.AreEqual(Side.Sell, matched.Fills[1].Order.Side);
        Assert.AreEqual(100, matched.Fills[1].Order.Price);
        Assert.IsNull(matched.Fills[1].Order.TriggerPrice);
        Assert.AreEqual(3, matched.Fills[1].Order.Quantity);
        Assert.AreEqual(3, matched.Fills[1].Order.FilledQuantity);
        Assert.AreEqual(0, matched.Fills[1].Order.RemainingQuantity);
    }

    [Test]
    public void LimitOrder_MatchAgainstOrdersAtSamePriceByTimeAfterDecreaseQuantity()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 5, 110);
        TimeProvider.SetCurrentTime(Now2);
        Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Buy, 5, 110);
        TimeProvider.SetCurrentTime(Now3);
        Book.UpdateOrder(CompanyId1, OrderId4, OrderId1, 4, 110);
        TimeProvider.SetCurrentTime(Now4);

        // act
        var events = Book.CreateLimitOrder(CompanyId3, OrderId3, new OrderValidity.Day(), Side.Sell, 8, 100);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(3, events.Count);

        var matched1 = events[1] as OrdersMatched;
        Assert.IsNotNull(matched1);
        Assert.AreEqual(Sec, matched1.Security);
        Assert.AreEqual(Now4, matched1.Time);
        Assert.AreEqual(110, matched1.Price);
        Assert.AreEqual(4, matched1.Quantity);
        Assert.IsNotNull(matched1.Fills);
        Assert.AreEqual(2, matched1.Fills.Count);

        Assert.AreEqual(Sec, matched1.Fills[0].Security);
        Assert.AreEqual(Now4, matched1.Fills[0].Time);
        Assert.AreEqual(CompanyId1, matched1.Fills[0].CompanyId);
        Assert.AreEqual(OrderId4, matched1.Fills[0].ClientOrderId);
        Assert.AreEqual(110, matched1.Fills[0].Price);
        Assert.AreEqual(4, matched1.Fills[0].Quantity);
        Assert.AreEqual(true, matched1.Fills[0].IsResting);
        Assert.AreEqual(CompanyId1, matched1.Fills[0].Order.CompanyId);
        Assert.AreEqual(OrderId4, matched1.Fills[0].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched1.Fills[0].Order.Security);
        Assert.AreEqual(Now1, matched1.Fills[0].Order.CreatedTime);
        Assert.AreEqual(Now3, matched1.Fills[0].Order.ModifiedTime);
        Assert.AreEqual(Now4, matched1.Fills[0].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Filled, matched1.Fills[0].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched1.Fills[0].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched1.Fills[0].Order.OrderValidity);
        Assert.AreEqual(Side.Buy, matched1.Fills[0].Order.Side);
        Assert.AreEqual(110, matched1.Fills[0].Order.Price);
        Assert.IsNull(matched1.Fills[0].Order.TriggerPrice);
        Assert.AreEqual(4, matched1.Fills[0].Order.Quantity);
        Assert.AreEqual(4, matched1.Fills[0].Order.FilledQuantity);
        Assert.AreEqual(0, matched1.Fills[0].Order.RemainingQuantity);

        Assert.AreEqual(Sec, matched1.Fills[1].Security);
        Assert.AreEqual(Now4, matched1.Fills[1].Time);
        Assert.AreEqual(CompanyId3, matched1.Fills[1].CompanyId);
        Assert.AreEqual(OrderId3, matched1.Fills[1].ClientOrderId);
        Assert.AreEqual(110, matched1.Fills[1].Price);
        Assert.AreEqual(4, matched1.Fills[1].Quantity);
        Assert.AreEqual(false, matched1.Fills[1].IsResting);
        Assert.AreEqual(CompanyId3, matched1.Fills[1].Order.CompanyId);
        Assert.AreEqual(OrderId3, matched1.Fills[1].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched1.Fills[1].Order.Security);
        Assert.AreEqual(Now4, matched1.Fills[1].Order.CreatedTime);
        Assert.AreEqual(Now4, matched1.Fills[1].Order.ModifiedTime);
        Assert.IsNull(matched1.Fills[1].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Working, matched1.Fills[1].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched1.Fills[1].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched1.Fills[1].Order.OrderValidity);
        Assert.AreEqual(Side.Sell, matched1.Fills[1].Order.Side);
        Assert.AreEqual(100, matched1.Fills[1].Order.Price);
        Assert.IsNull(matched1.Fills[1].Order.TriggerPrice);
        Assert.AreEqual(8, matched1.Fills[1].Order.Quantity);
        Assert.AreEqual(4, matched1.Fills[1].Order.FilledQuantity);
        Assert.AreEqual(4, matched1.Fills[1].Order.RemainingQuantity);

        var matched2 = events[2] as OrdersMatched;
        Assert.IsNotNull(matched2);
        Assert.AreEqual(Sec, matched2.Security);
        Assert.AreEqual(Now4, matched2.Time);
        Assert.AreEqual(110, matched2.Price);
        Assert.AreEqual(4, matched2.Quantity);
        Assert.IsNotNull(matched2.Fills);
        Assert.AreEqual(2, matched2.Fills.Count);

        Assert.AreEqual(Sec, matched2.Fills[0].Security);
        Assert.AreEqual(Now4, matched2.Fills[0].Time);
        Assert.AreEqual(CompanyId2, matched2.Fills[0].CompanyId);
        Assert.AreEqual(OrderId2, matched2.Fills[0].ClientOrderId);
        Assert.AreEqual(110, matched2.Fills[0].Price);
        Assert.AreEqual(4, matched2.Fills[0].Quantity);
        Assert.AreEqual(true, matched2.Fills[0].IsResting);
        Assert.AreEqual(CompanyId2, matched2.Fills[0].Order.CompanyId);
        Assert.AreEqual(OrderId2, matched2.Fills[0].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched2.Fills[0].Order.Security);
        Assert.AreEqual(Now2, matched2.Fills[0].Order.CreatedTime);
        Assert.AreEqual(Now2, matched2.Fills[0].Order.ModifiedTime);
        Assert.IsNull(matched2.Fills[0].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Working, matched2.Fills[0].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched2.Fills[0].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched2.Fills[0].Order.OrderValidity);
        Assert.AreEqual(Side.Buy, matched2.Fills[0].Order.Side);
        Assert.AreEqual(110, matched2.Fills[0].Order.Price);
        Assert.IsNull(matched2.Fills[0].Order.TriggerPrice);
        Assert.AreEqual(5, matched2.Fills[0].Order.Quantity);
        Assert.AreEqual(4, matched2.Fills[0].Order.FilledQuantity);
        Assert.AreEqual(1, matched2.Fills[0].Order.RemainingQuantity);

        Assert.AreEqual(Sec, matched2.Fills[1].Security);
        Assert.AreEqual(Now4, matched2.Fills[1].Time);
        Assert.AreEqual(CompanyId3, matched2.Fills[1].CompanyId);
        Assert.AreEqual(OrderId3, matched2.Fills[1].ClientOrderId);
        Assert.AreEqual(110, matched2.Fills[1].Price);
        Assert.AreEqual(4, matched2.Fills[1].Quantity);
        Assert.AreEqual(false, matched2.Fills[1].IsResting);
        Assert.AreEqual(CompanyId3, matched2.Fills[1].Order.CompanyId);
        Assert.AreEqual(OrderId3, matched2.Fills[1].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched2.Fills[1].Order.Security);
        Assert.AreEqual(Now4, matched2.Fills[1].Order.CreatedTime);
        Assert.AreEqual(Now4, matched2.Fills[1].Order.ModifiedTime);
        Assert.AreEqual(Now4, matched2.Fills[1].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Filled, matched2.Fills[1].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched2.Fills[1].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched2.Fills[1].Order.OrderValidity);
        Assert.AreEqual(Side.Sell, matched2.Fills[1].Order.Side);
        Assert.AreEqual(100, matched2.Fills[1].Order.Price);
        Assert.IsNull(matched2.Fills[1].Order.TriggerPrice);
        Assert.AreEqual(8, matched2.Fills[1].Order.Quantity);
        Assert.AreEqual(8, matched2.Fills[1].Order.FilledQuantity);
        Assert.AreEqual(0, matched2.Fills[1].Order.RemainingQuantity);
    }

    [Test]
    public void LimitOrder_MatchGoodTilCanceledAfterReopen()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.GoodTilCanceled(), Side.Buy, 5, 100);
        TimeProvider.SetCurrentTime(Now2);
        Book.UpdateStatus(OrderBookStatus.Closed);
        TimeProvider.SetCurrentTime(Now3);
        Book.UpdateStatus(OrderBookStatus.Open);
        TimeProvider.SetCurrentTime(Now4);

        // act
        var events = Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Sell, 8, 100);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(2, events.Count);

        var matched = events[1] as OrdersMatched;
        Assert.IsNotNull(matched);
        Assert.AreEqual(Sec, matched.Security);
        Assert.AreEqual(Now4, matched.Time);
        Assert.AreEqual(100, matched.Price);
        Assert.AreEqual(5, matched.Quantity);
        Assert.IsNotNull(matched.Fills);
        Assert.AreEqual(2, matched.Fills.Count);

        Assert.AreEqual(Sec, matched.Fills[0].Security);
        Assert.AreEqual(Now4, matched.Fills[0].Time);
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
        Assert.AreEqual(Now4, matched.Fills[0].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Filled, matched.Fills[0].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched.Fills[0].Order.Type);
        Assert.AreEqual(new OrderValidity.GoodTilCanceled(), matched.Fills[0].Order.OrderValidity);
        Assert.AreEqual(Side.Buy, matched.Fills[0].Order.Side);
        Assert.AreEqual(100, matched.Fills[0].Order.Price);
        Assert.IsNull(matched.Fills[0].Order.TriggerPrice);
        Assert.AreEqual(5, matched.Fills[0].Order.Quantity);
        Assert.AreEqual(5, matched.Fills[0].Order.FilledQuantity);
        Assert.AreEqual(0, matched.Fills[0].Order.RemainingQuantity);

        Assert.AreEqual(Sec, matched.Fills[1].Security);
        Assert.AreEqual(Now4, matched.Fills[1].Time);
        Assert.AreEqual(CompanyId2, matched.Fills[1].CompanyId);
        Assert.AreEqual(OrderId2, matched.Fills[1].ClientOrderId);
        Assert.AreEqual(100, matched.Fills[1].Price);
        Assert.AreEqual(5, matched.Fills[1].Quantity);
        Assert.AreEqual(false, matched.Fills[1].IsResting);
        Assert.AreEqual(CompanyId2, matched.Fills[1].Order.CompanyId);
        Assert.AreEqual(OrderId2, matched.Fills[1].Order.ClientOrderId);
        Assert.AreEqual(Sec, matched.Fills[1].Order.Security);
        Assert.AreEqual(Now4, matched.Fills[1].Order.CreatedTime);
        Assert.AreEqual(Now4, matched.Fills[1].Order.ModifiedTime);
        Assert.IsNull(matched.Fills[1].Order.CompletedTime);
        Assert.AreEqual(OrderStatus.Working, matched.Fills[1].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched.Fills[1].Order.Type);
        Assert.AreEqual(new OrderValidity.Day(), matched.Fills[1].Order.OrderValidity);
        Assert.AreEqual(Side.Sell, matched.Fills[1].Order.Side);
        Assert.AreEqual(100, matched.Fills[1].Order.Price);
        Assert.IsNull(matched.Fills[1].Order.TriggerPrice);
        Assert.AreEqual(8, matched.Fills[1].Order.Quantity);
        Assert.AreEqual(5, matched.Fills[1].Order.FilledQuantity);
        Assert.AreEqual(3, matched.Fills[1].Order.RemainingQuantity);
    }

    [Test]
    public void MarketClosed_Rejected()
    {
        // arrange
        // act
        var events = Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 3, 100);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(1, events.Count);
        var rejected = events[0] as CreateOrderRejected;
        Assert.IsNotNull(rejected);
        Assert.AreEqual(Sec, rejected.Security);
        Assert.AreEqual(Now1, rejected.Time);
        Assert.AreEqual(OrderRejectedReason.MarketClosed, rejected.Reason);
        Assert.AreEqual(CompanyId1, rejected.CompanyId);
        Assert.AreEqual(OrderId1, rejected.ClientOrderId);
        Assert.IsNull(rejected.ExchangeOrderId, "no order was ever created, so there's no ExchangeOrderId to report");
    }

    [Test]
    public void MarketOrder_MarketOrdersNotAccepted_Rejected()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.PreOpen);

        // act
        var events = Book.CreateMarketOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 3);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(1, events.Count);
        var rejected = events[0] as CreateOrderRejected;
        Assert.IsNotNull(rejected);
        Assert.AreEqual(Sec, rejected.Security);
        Assert.AreEqual(Now1, rejected.Time);
        Assert.AreEqual(CompanyId1, rejected.CompanyId);
        Assert.AreEqual(OrderId1, rejected.ClientOrderId);
        Assert.AreEqual(OrderRejectedReason.MarketOrdersNotAccepted, rejected.Reason);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void InvalidQuantity_Rejected(int quantity)
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);

        // act
        var events = Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, quantity, 100);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(1, events.Count);
        var rejected = events[0] as CreateOrderRejected;
        Assert.IsNotNull(rejected);
        Assert.AreEqual(Sec, rejected.Security);
        Assert.AreEqual(Now1, rejected.Time);
        Assert.AreEqual(OrderRejectedReason.InvalidQuantity, rejected.Reason);
        Assert.AreEqual(CompanyId1, rejected.CompanyId);
        Assert.AreEqual(OrderId1, rejected.ClientOrderId);
    }

    [TestCase(8)]
    [TestCase(-8)]
    [TestCase(-108)]
    [TestCase(10.01)]
    public void InvalidPriceIncrement_Rejected(decimal price)
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);

        // act
        var events = Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 6, price);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(1, events.Count);
        var rejected = events[0] as CreateOrderRejected;
        Assert.IsNotNull(rejected);
        Assert.AreEqual(Sec, rejected.Security);
        Assert.AreEqual(Now1, rejected.Time);
        Assert.AreEqual(OrderRejectedReason.InvalidPriceIncrement, rejected.Reason);
        Assert.AreEqual(CompanyId1, rejected.CompanyId);
        Assert.AreEqual(OrderId1, rejected.ClientOrderId);
    }

    [TestCase(-10)]
    [TestCase(-100)]
    public void NegativePriceOnTick_Success(decimal price)
    {
        // arrange - negative prices are legitimate (e.g. calendar spreads, or the 2020 WTI
        // crude event) and must still be accepted as long as they're on a valid tick
        Book.UpdateStatus(OrderBookStatus.Open);

        // act
        var events = Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 6, price);

        // assert
        Assert.IsNotNull(events);
        var created = events[0] as CreateOrderConfirmed;
        Assert.IsNotNull(created);
        Assert.AreEqual(price, created.Order.Price);
    }

    [TestCase(8)]
    [TestCase(-8)]
    [TestCase(-108)]
    [TestCase(10.01)]
    public void InvalidTriggerPriceIncrement_Rejected(decimal triggerPrice)
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);

        // act
        var events = Book.CreateStopMarketOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 6, triggerPrice);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(1, events.Count);
        var rejected = events[0] as CreateOrderRejected;
        Assert.IsNotNull(rejected);
        Assert.AreEqual(Sec, rejected.Security);
        Assert.AreEqual(Now1, rejected.Time);
        Assert.AreEqual(OrderRejectedReason.InvalidPriceIncrement, rejected.Reason);
        Assert.AreEqual(CompanyId1, rejected.CompanyId);
        Assert.AreEqual(OrderId1, rejected.ClientOrderId);
    }

    [Test]
    public void StopOrder_TriggerPriceMustBeLessThanPrice_Rejected()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);

        // act
        var events = Book.CreateStopLimitOrder(CompanyId3, OrderId3, new OrderValidity.Day(), Side.Buy, 3, 90, 100);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(1, events.Count);
        var rejected = events[0] as CreateOrderRejected;
        Assert.IsNotNull(rejected);
        Assert.AreEqual(Sec, rejected.Security);
        Assert.AreEqual(Now1, rejected.Time);
        Assert.AreEqual(CompanyId3, rejected.CompanyId);
        Assert.AreEqual(OrderId3, rejected.ClientOrderId);
        Assert.AreEqual(OrderRejectedReason.TriggerPriceMustBeLessThanPrice, rejected.Reason);
    }

    [Test]
    public void StopOrder_TriggerPriceMustBeGreaterThanPrice_Rejected()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);

        // act
        var events = Book.CreateStopLimitOrder(CompanyId3, OrderId3, new OrderValidity.Day(), Side.Sell, 3, 110, 100);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(1, events.Count);
        var rejected = events[0] as CreateOrderRejected;
        Assert.IsNotNull(rejected);
        Assert.AreEqual(Sec, rejected.Security);
        Assert.AreEqual(Now1, rejected.Time);
        Assert.AreEqual(CompanyId3, rejected.CompanyId);
        Assert.AreEqual(OrderId3, rejected.ClientOrderId);
        Assert.AreEqual(OrderRejectedReason.TriggerPriceMustBeGreaterThanPrice, rejected.Reason);
    }

    [Test]
    public void StopOrder_NoLastTradedPrice_Rejected()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);

        // act
        var events = Book.CreateStopMarketOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 3, 100);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(1, events.Count);
        var rejected = events[0] as CreateOrderRejected;
        Assert.IsNotNull(rejected);
        Assert.AreEqual(Sec, rejected.Security);
        Assert.AreEqual(Now1, rejected.Time);
        Assert.AreEqual(CompanyId1, rejected.CompanyId);
        Assert.AreEqual(OrderId1, rejected.ClientOrderId);
        Assert.AreEqual(OrderRejectedReason.NoLastTradedPrice, rejected.Reason);
    }

    [TestCase(90)]
    [TestCase(100)]
    public void StopOrder_TriggerPriceMustBeGreaterThanLastTraded_Rejected(int price)
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 3, 100);
        Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Sell, 3, 100);

        // act
        var events = Book.CreateStopMarketOrder(CompanyId3, OrderId3, new OrderValidity.Day(), Side.Buy, 3, price);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(1, events.Count);
        var rejected = events[0] as CreateOrderRejected;
        Assert.IsNotNull(rejected);
        Assert.AreEqual(Sec, rejected.Security);
        Assert.AreEqual(Now1, rejected.Time);
        Assert.AreEqual(CompanyId3, rejected.CompanyId);
        Assert.AreEqual(OrderId3, rejected.ClientOrderId);
        Assert.AreEqual(OrderRejectedReason.TriggerPriceMustBeGreaterThanLastTradedPrice, rejected.Reason);
    }

    [TestCase(110)]
    [TestCase(100)]
    public void StopOrder_TriggerPriceMustBeLessThanLastTraded_Rejected(int price)
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 3, 100);
        Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Sell, 3, 100);

        // act
        var events = Book.CreateStopMarketOrder(CompanyId3, OrderId3, new OrderValidity.Day(), Side.Sell, 3, price);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(1, events.Count);
        var rejected = events[0] as CreateOrderRejected;
        Assert.IsNotNull(rejected);
        Assert.AreEqual(Sec, rejected.Security);
        Assert.AreEqual(Now1, rejected.Time);
        Assert.AreEqual(CompanyId3, rejected.CompanyId);
        Assert.AreEqual(OrderId3, rejected.ClientOrderId);
        Assert.AreEqual(OrderRejectedReason.TriggerPriceMustBeLessThanLastTradedPrice, rejected.Reason);
    }

    [Test]
    public void MarketOrder_EmptyBook_Rejected()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);

        // act
        var events = Book.CreateMarketOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 3);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(1, events.Count);
        var rejected = events[0] as CreateOrderRejected;
        Assert.IsNotNull(rejected);
        Assert.AreEqual(Sec, rejected.Security);
        Assert.AreEqual(Now1, rejected.Time);
        Assert.AreEqual(CompanyId1, rejected.CompanyId);
        Assert.AreEqual(OrderId1, rejected.ClientOrderId);
        Assert.AreEqual(OrderRejectedReason.NoOrdersToMatchMarketOrder, rejected.Reason);
    }

    [Test]
    public void DuplicateOrderId_Rejected()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 3, 100);

        // act
        var events = Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 5, 100);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(1, events.Count);
        var rejected = events[0] as CreateOrderRejected;
        Assert.IsNotNull(rejected);
        Assert.AreEqual(Sec, rejected.Security);
        Assert.AreEqual(Now1, rejected.Time);
        Assert.AreEqual(CompanyId1, rejected.CompanyId);
        Assert.AreEqual(OrderId1, rejected.ClientOrderId);
        Assert.AreEqual(OrderRejectedReason.OrderInBook, rejected.Reason);
    }

    [Test]
    public void OrderIdReusedAfterCancel_Rejected()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 3, 100);
        Book.CancelOrder(CompanyId1, OrderId5, OrderId1);

        // act
        var events = Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 5, 100);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(1, events.Count);
        var rejected = events[0] as CreateOrderRejected;
        Assert.IsNotNull(rejected);
        Assert.AreEqual(CompanyId1, rejected.CompanyId);
        Assert.AreEqual(OrderId1, rejected.ClientOrderId);
        Assert.AreEqual(OrderRejectedReason.OrderIdAlreadyUsed, rejected.Reason);
    }

    [Test]
    public void OrderIdReusedAfterFullFill_Rejected()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 3, 100);
        Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Buy, 3, 100); // fully fills OrderId1

        // act
        var events = Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 5, 100);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(1, events.Count);
        var rejected = events[0] as CreateOrderRejected;
        Assert.IsNotNull(rejected);
        Assert.AreEqual(CompanyId1, rejected.CompanyId);
        Assert.AreEqual(OrderId1, rejected.ClientOrderId);
        Assert.AreEqual(OrderRejectedReason.OrderIdAlreadyUsed, rejected.Reason);
    }

    [Test]
    public void ClientOrderId_ReusedBySameClient_Rejected()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 3, 100);

        // act
        var events = Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 5, 100);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(1, events.Count);
        var rejected = events[0] as CreateOrderRejected;
        Assert.IsNotNull(rejected);
        Assert.AreEqual(OrderRejectedReason.OrderInBook, rejected.Reason);
    }

    [Test]
    public void ClientOrderId_SameValueDifferentClient_Success()
    {
        // arrange - the same client-supplied id is only required to be unique per client,
        // not book-wide, since uniqueness is scoped by the (CompanyId, ClientOrderId) pair
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 3, 100);

        // act - same side/price as CompanyId1's order, so it rests instead of matching
        var events = Book.CreateLimitOrder(CompanyId2, OrderId1, new OrderValidity.Day(), Side.Buy, 5, 100);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(1, events.Count);
        var created = events[0] as CreateOrderConfirmed;
        Assert.IsNotNull(created);
        Assert.AreEqual(CompanyId2, created.Order.CompanyId);
        Assert.AreEqual(OrderId1, created.Order.ClientOrderId);
    }

    [Test]
    public void GoodTilDateOrder_Success()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        var goodTilDate = DateOnly.FromDateTime(Now1).AddDays(7);

        // act
        var events = Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.GoodTilDate { Date = goodTilDate }, Side.Buy, 3, 100);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(1, events.Count);
        var created = events[0] as CreateOrderConfirmed;
        Assert.IsNotNull(created);
        Assert.AreEqual(new OrderValidity.GoodTilDate { Date = goodTilDate }, created.Order.OrderValidity);
    }

    [Test]
    public void GoodTilDateOrder_SameDayAsToday_Success()
    {
        // arrange - a good-til-date order dated today is valid and behaves like a Day order for this session
        Book.UpdateStatus(OrderBookStatus.Open);
        var goodTilDate = DateOnly.FromDateTime(Now1);

        // act
        var events = Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.GoodTilDate { Date = goodTilDate }, Side.Buy, 3, 100);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(1, events.Count);
        var created = events[0] as CreateOrderConfirmed;
        Assert.IsNotNull(created);
        Assert.AreEqual(new OrderValidity.GoodTilDate { Date = goodTilDate }, created.Order.OrderValidity);
    }

    [Test]
    public void GoodTilDateOrder_DateInPast_Rejected()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        var goodTilDate = DateOnly.FromDateTime(Now1).AddDays(-1);

        // act
        var events = Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.GoodTilDate { Date = goodTilDate }, Side.Buy, 3, 100);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(1, events.Count);
        var rejected = events[0] as CreateOrderRejected;
        Assert.IsNotNull(rejected);
        Assert.AreEqual(CompanyId1, rejected.CompanyId);
        Assert.AreEqual(OrderId1, rejected.ClientOrderId);
        Assert.AreEqual(OrderRejectedReason.InvalidExpireDate, rejected.Reason);
    }

    [TestCase(null)]
    [TestCase("")]
    public void ClientOrderId_Missing_Rejected(string clientOrderId)
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);

        // act
        var events = Book.CreateLimitOrder(CompanyId1, clientOrderId, new OrderValidity.Day(), Side.Buy, 3, 100);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(1, events.Count);
        var rejected = events[0] as CreateOrderRejected;
        Assert.IsNotNull(rejected);
        Assert.AreEqual(CompanyId1, rejected.CompanyId);
        Assert.AreEqual(OrderRejectedReason.ClientOrderIdRequired, rejected.Reason);
    }

    [TestCase(20)]
    public void ClientOrderId_AtMaxLength_Success(int length)
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        var clientOrderId = new string('a', length);

        // act
        var events = Book.CreateLimitOrder(CompanyId1, clientOrderId, new OrderValidity.Day(), Side.Buy, 3, 100);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(1, events.Count);
        var created = events[0] as CreateOrderConfirmed;
        Assert.IsNotNull(created);
        Assert.AreEqual(clientOrderId, created.Order.ClientOrderId);
    }

    [TestCase(21)]
    [TestCase(36)]
    public void ClientOrderId_TooLong_Rejected(int length)
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        var clientOrderId = new string('a', length);

        // act
        var events = Book.CreateLimitOrder(CompanyId1, clientOrderId, new OrderValidity.Day(), Side.Buy, 3, 100);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(1, events.Count);
        var rejected = events[0] as CreateOrderRejected;
        Assert.IsNotNull(rejected);
        Assert.AreEqual(CompanyId1, rejected.CompanyId);
        Assert.AreEqual(OrderRejectedReason.ClientOrderIdTooLong, rejected.Reason);
    }

    [TestCase(null)]
    [TestCase("")]
    public void CompanyId_Missing_Rejected(string companyId)
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);

        // act
        var events = Book.CreateLimitOrder(companyId, OrderId1, new OrderValidity.Day(), Side.Buy, 3, 100);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(1, events.Count);
        var rejected = events[0] as CreateOrderRejected;
        Assert.IsNotNull(rejected);
        Assert.AreEqual(OrderId1, rejected.ClientOrderId);
        Assert.AreEqual(OrderRejectedReason.CompanyIdRequired, rejected.Reason);
    }

    [TestCase(20)]
    public void CompanyId_AtMaxLength_Success(int length)
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        var companyId = new string('a', length);

        // act
        var events = Book.CreateLimitOrder(companyId, OrderId1, new OrderValidity.Day(), Side.Buy, 3, 100);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(1, events.Count);
        var created = events[0] as CreateOrderConfirmed;
        Assert.IsNotNull(created);
        Assert.AreEqual(companyId, created.Order.CompanyId);
    }

    [TestCase(21)]
    [TestCase(36)]
    public void CompanyId_TooLong_Rejected(int length)
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        var companyId = new string('a', length);

        // act
        var events = Book.CreateLimitOrder(companyId, OrderId1, new OrderValidity.Day(), Side.Buy, 3, 100);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(1, events.Count);
        var rejected = events[0] as CreateOrderRejected;
        Assert.IsNotNull(rejected);
        Assert.AreEqual(OrderId1, rejected.ClientOrderId);
        Assert.AreEqual(OrderRejectedReason.CompanyIdTooLong, rejected.Reason);
    }

}
