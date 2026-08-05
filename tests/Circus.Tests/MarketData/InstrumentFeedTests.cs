using Circus.Events;
using Circus.MarketData;
using Circus.Time;
using Circus.Tests.Helpers;
using NUnit.Framework;

namespace Circus.Tests.MarketData;

// InstrumentFeed is the bundle of producers a venue publishes for one instrument. The producers
// themselves are tested individually; what is worth holding here is that the bundle runs all of
// them, that everything it emits says which instrument it is about, and that its order within a
// call is fixed rather than incidental.
[TestFixture]
public class InstrumentFeedTests
{
    private static readonly Instrument Gold = new("GCZ6", 10, 10);
    private static readonly DateTime Now1 = new(2000, 1, 1, 12, 0, 0);

    [Test]
    public void Process_ATrade_ProducesFromEveryProducerThatHasSomethingToSay()
    {
        // arrange
        var (feed, book) = Feed();
        feed.Process(book.UpdateStatus(OrderBookStatus.Open));
        feed.Process(book.CreateLimitOrder("Company1", "Order1", new OrderValidity.Day(), Side.Buy, 3, 100));

        // act - the crossing order: a print, a level change, and depth deltas, all from one action
        var data = feed.Process(
            book.CreateLimitOrder("Company2", "Order2", new OrderValidity.Day(), Side.Sell, 3, 100));

        // assert
        Assert.IsNotEmpty(data.OfType<TradeDataEvent>());
        Assert.IsNotEmpty(data.OfType<MarketByPriceDeltaEvent>());
        Assert.IsNotEmpty(data.OfType<MarketByOrderDeltaEvent>());
    }

    [Test]
    public void Process_AStatusChange_ProducesTheStatusMessage()
    {
        var (feed, book) = Feed();

        var data = feed.Process(book.UpdateStatus(OrderBookStatus.Open));

        var status = data.OfType<InstrumentStatusDataEvent>().Single();
        Assert.AreEqual(OrderBookStatus.Open, status.Status);
    }

    [Test]
    public void Process_EverythingItEmitsCarriesTheInstrument()
    {
        // arrange
        var (feed, book) = Feed();
        var all = new List<MarketDataEvent>();

        // act - a pre-open auction, so the indicative producer contributes too
        all.AddRange(feed.Process(book.UpdateStatus(OrderBookStatus.PreOpen)));
        all.AddRange(feed.Process(
            book.CreateLimitOrder("Company1", "Order1", new OrderValidity.Day(), Side.Buy, 3, 100)));
        all.AddRange(feed.Process(
            book.CreateLimitOrder("Company2", "Order2", new OrderValidity.Day(), Side.Sell, 3, 100)));
        all.AddRange(feed.Process(book.UpdateStatus(OrderBookStatus.Open)));

        // assert - this is what lets several instruments share a channel
        Assert.IsNotEmpty(all);
        Assert.IsNotEmpty(all.OfType<IndicativePriceDataEvent>(), "expected an auction quote");
        Assert.IsTrue(all.All(d => d.Symbol == Gold.Symbol),
            "every message must say which instrument it is about");
    }

    [Test]
    public void Process_OrdersItsOutputByProducerRatherThanIncidentally()
    {
        // arrange
        var (feed, book) = Feed();
        feed.Process(book.UpdateStatus(OrderBookStatus.Open));
        feed.Process(book.CreateLimitOrder("Company1", "Order1", new OrderValidity.Day(), Side.Buy, 3, 100));

        // act
        var data = feed.Process(
            book.CreateLimitOrder("Company2", "Order2", new OrderValidity.Day(), Side.Sell, 3, 100));

        // assert - trades ahead of levels ahead of depth. Every event in one dispatch shares an
        // instant, so there is no time order among them to preserve and the bundle's own order is
        // what a subscriber gets. Fixed, so it is the same on every run.
        var kinds = data.Select(d => d.GetType().Name).Distinct().ToList();
        Assert.AreEqual(
            new[] {nameof(TradeDataEvent), nameof(MarketByPriceDeltaEvent), nameof(MarketByOrderDeltaEvent)},
            kinds);
    }

    [Test]
    public void Process_NoEvents_ProducesNothing()
    {
        var (feed, _) = Feed();

        Assert.IsEmpty(feed.Process(Array.Empty<OrderBookEvent>()));
    }

    private static (InstrumentFeed Feed, IOrderBook Book) Feed()
    {
        var book = new TimestampingOrderBook(Gold, new ManualClock(Now1));
        return (new InstrumentFeed(Gold.Symbol), book);
    }
}
