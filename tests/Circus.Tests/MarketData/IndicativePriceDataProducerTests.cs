using Circus.Actions;
using Circus.Events;
using Circus.MarketData;
using Circus.Time;
using NUnit.Framework;

namespace Circus.Tests.MarketData;

public class IndicativePriceDataProducerTests
{
    private static readonly Security Sec = new("GCZ6", SecurityType.Future, 10, 10);

    private static readonly DateTime Now1 = new(2000, 1, 1, 12, 0, 0);
    private static readonly DateTime Now2 = new(2000, 1, 1, 12, 1, 0);

    private static ManualClock Clock;
    private static IOrderBook Book;

    [SetUp]
    public void SetUp()
    {
        Clock = new ManualClock(Now1);
        Book = new TimestampingOrderBook(Sec, Clock);
    }

    private static IList<IndicativePriceDataEvent> Publish(IndicativePriceDataProducer producer,
        IReadOnlyList<OrderBookEvent> bookEvents) =>
        producer.Process(Book, bookEvents);

    [Test]
    public void CrossedPreOpenBook_PublishesTheQuote()
    {
        var producer = new IndicativePriceDataProducer();
        Publish(producer, Book.UpdateStatus(OrderBookStatus.PreOpen));
        Publish(producer, Book.CreateLimitOrder("Company1", "Order1", new OrderValidity.Day(), Side.Buy, 5, 100));

        var events = Publish(producer,
            Book.CreateLimitOrder("Company2", "Order2", new OrderValidity.Day(), Side.Sell, 3, 100));

        Assert.AreEqual(1, events.Count);
        Assert.AreEqual(Now1, events[0].Time);
        Assert.AreEqual(100, events[0].Price);
        Assert.AreEqual(3, events[0].Quantity);
    }

    [Test]
    public void UncrossedBook_PublishesNothing()
    {
        var producer = new IndicativePriceDataProducer();
        Publish(producer, Book.UpdateStatus(OrderBookStatus.PreOpen));

        var events = Publish(producer,
            Book.CreateLimitOrder("Company1", "Order1", new OrderValidity.Day(), Side.Buy, 5, 100));

        Assert.IsEmpty(events);
    }

    [Test]
    public void ContinuousTrading_PublishesNothing()
    {
        var producer = new IndicativePriceDataProducer();
        Publish(producer, Book.UpdateStatus(OrderBookStatus.Open));
        Publish(producer, Book.CreateLimitOrder("Company1", "Order1", new OrderValidity.Day(), Side.Buy, 5, 100));

        var events = Publish(producer,
            Book.CreateLimitOrder("Company2", "Order2", new OrderValidity.Day(), Side.Sell, 3, 100));

        Assert.IsEmpty(events, "price-time has no single price it would print at");
    }

    [Test]
    public void AuctionPrints_WithdrawsTheQuote()
    {
        var producer = new IndicativePriceDataProducer();
        Publish(producer, Book.UpdateStatus(OrderBookStatus.PreOpen));
        Publish(producer, Book.CreateLimitOrder("Company1", "Order1", new OrderValidity.Day(), Side.Buy, 5, 100));
        Publish(producer, Book.CreateLimitOrder("Company2", "Order2", new OrderValidity.Day(), Side.Sell, 5, 100));

        Clock.SetCurrentTime(Now2);
        var events = Publish(producer, Book.UpdateStatus(OrderBookStatus.Open));

        Assert.AreEqual(1, events.Count);
        Assert.AreEqual(Now2, events[0].Time);
        Assert.IsNull(events[0].Price, "there is no auction left to quote once it has printed");
        Assert.AreEqual(0, events[0].Quantity);
    }
}
