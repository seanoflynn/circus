using Circus.Actions;
using Circus.MarketData;
using Circus.Time;
using NUnit.Framework;

namespace Circus.Tests.MarketData;

public class TradeDataProducerTests
{
    [Test]
    public void TradeDataProducer_Traded_FiresEvent()
    {
        // arrange
        var gold = new Instrument("GCZ6", 10, 10);
        var now = new DateTime(2000, 1, 1, 12, 0, 0);
        var clock = new ManualClock(now);
        var producer = new TradeDataProducer();

        var book = new TimestampingOrderBook(gold, clock);
        book.UpdateStatus(OrderBookStatus.Open);
        book.CreateLimitOrder("Company1", "Order1", new OrderValidity.Day(), Side.Buy, 3, 100);
        var bookEvents =
            book.CreateLimitOrder("Company2", "Order2", new OrderValidity.Day(), Side.Sell, 3, 100);

        // act
        var events = producer.Process(bookEvents);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(1, events.Count);
        Assert.AreEqual(now, events[0].Time);
        Assert.AreEqual(100, events[0].Price);
        Assert.AreEqual(3, events[0].Quantity);
    }
}
