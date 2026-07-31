using Circus.Events;
using Circus.MarketData;
using Circus.Time;
using NUnit.Framework;

namespace Circus.Tests.MarketData;

// SecurityFeed is the bundle of producers a venue publishes for one instrument. The producers
// themselves are tested individually; what is worth holding here is that the bundle runs all of
// them, that everything it emits says which instrument it is about, and that its order within a
// call is fixed rather than incidental.
[TestFixture]
public class SecurityFeedTests
{
    private static readonly Security Sec = new("GCZ6", 10, 10);
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
        Assert.IsNotEmpty(data.OfType<TradedDataEvent>());
        Assert.IsNotEmpty(data.OfType<LevelsDataEvent>());
        Assert.IsNotEmpty(data.OfType<OrderBookDeltaEvent>());
    }

    [Test]
    public void Process_AStatusChange_ProducesTheStatusMessage()
    {
        var (feed, book) = Feed();

        var data = feed.Process(book.UpdateStatus(OrderBookStatus.Open));

        var status = data.OfType<SecurityStatusDataEvent>().Single();
        Assert.AreEqual(OrderBookStatus.Open, status.Status);
    }

    [Test]
    public void Process_EverythingItEmitsCarriesTheSecurity()
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
        Assert.IsTrue(all.All(d => ReferenceEquals(d.Security, Sec)),
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
            new[] {nameof(TradedDataEvent), nameof(LevelsDataEvent), nameof(OrderBookDeltaEvent)},
            kinds);
    }

    [Test]
    public void Process_NoEvents_ProducesNothing()
    {
        var (feed, _) = Feed();

        Assert.IsEmpty(feed.Process(Array.Empty<OrderBookEvent>()));
    }

    private static (SecurityFeed Feed, IOrderBook Book) Feed()
    {
        var book = new TimestampingOrderBook(Sec, new ManualClock(Now1));
        return (new SecurityFeed(Sec, maxLevels: 10), book);
    }
}
