using Circus.Actions;
using Circus.MarketData;
using Circus.Tests.Helpers;
using NUnit.Framework;

namespace Circus.Tests.MarketData;

// A channel is a subset of the venue in two directions: which instruments it carries, and which
// products it carries about them. This is the second, and it is what lets one engine wear
// different venues - CME carrying by-price and by-order together, Eurex splitting them across
// EOBI and EMDI with state on both, an ITCH-shaped venue carrying by-order alone.
//
// One flag per product, not per message: a product's incremental and snapshot halves are the same
// thing seen twice, so a feed carrying market by price carries both its deltas and its images.
public class FeedProductsTests
{
    private static readonly Instrument Gold = new("GCZ6", 10, 10);

    private static readonly DateTime Now1 = new(2000, 1, 1, 12, 0, 0);
    private static readonly DateTime Now2 = new(2000, 1, 1, 12, 1, 0);
    private static readonly DateTime Now3 = new(2000, 1, 1, 12, 2, 0);
    private static readonly DateTime Now4 = new(2000, 1, 1, 12, 3, 0);

    // A trade, which is the action that moves every product at once - depth, orders, and a print -
    // so what comes out is decided by the flags rather than by the action being too quiet to say.
    private static (IReadOnlyList<MarketDataEvent> Incremental, IReadOnlyList<MarketDataEvent> Snapshot)
        Publish(FeedProducts products)
    {
        var feed = new InstrumentFeed(Gold.Symbol, products);
        var book = new OrderBook(Gold);

        feed.Process(book.UpdateStatus(OrderBookStatus.Open, time: Now1));
        feed.Process(book.CreateLimitOrder("C1", "O1", new OrderValidity.Day(), Side.Buy, 3, 100,
            time: Now2));

        var incremental = feed.Process(
            book.CreateLimitOrder("C2", "O2", new OrderValidity.Day(), Side.Sell, 3, 100, time: Now3));

        var snapshot = feed.Snapshot(
            book.Process(new PublishSnapshot {Symbol = Gold.Symbol, Time = Now4}));

        return (incremental, snapshot);
    }

    private static IEnumerable<string> Kinds(IEnumerable<MarketDataEvent> data) =>
        data.Select(d => d.GetType().Name).Distinct();

    [Test]
    public void ByDefault_AFeedCarriesEverything()
    {
        var (incremental, snapshot) = Publish(FeedProducts.All);

        Assert.AreEqual(new[]
            {
                nameof(TradeDataEvent), nameof(MarketByPriceDeltaEvent), nameof(MarketByOrderDeltaEvent)
            },
            Kinds(incremental).ToArray());
        Assert.AreEqual(new[]
            {
                nameof(InstrumentStatusDataEvent), nameof(LevelsDataEvent), nameof(OrdersDataEvent),
                nameof(IndicativePriceDataEvent)
            },
            Kinds(snapshot).ToArray());
    }

    [Test]
    public void ADepthOnlyFeed_CarriesDepthAndNothingElse()
    {
        var (incremental, snapshot) = Publish(FeedProducts.ByPrice);

        Assert.AreEqual(new[] {nameof(MarketByPriceDeltaEvent)}, Kinds(incremental).ToArray());
        Assert.AreEqual(new[] {nameof(LevelsDataEvent)}, Kinds(snapshot).ToArray(),
            "one flag covers both halves of a product");
    }

    [Test]
    public void AnOrderByOrderOnlyFeed_CarriesOrdersAndNothingElse()
    {
        var (incremental, snapshot) = Publish(FeedProducts.ByOrder);

        Assert.AreEqual(new[] {nameof(MarketByOrderDeltaEvent)}, Kinds(incremental).ToArray());
        Assert.AreEqual(new[] {nameof(OrdersDataEvent)}, Kinds(snapshot).ToArray());
    }

    [Test]
    public void ATradesOnlyFeed_PublishesPrintsAndNoBook()
    {
        var (incremental, snapshot) = Publish(FeedProducts.Trades);

        Assert.AreEqual(new[] {nameof(TradeDataEvent)}, Kinds(incremental).ToArray());
        Assert.IsEmpty(snapshot, "there is no snapshot of a stream of prints");
    }

    // Eurex's split, as far as this models it: order by order with state on one channel, depth
    // with trades and state on another, and the same instrument on both.
    [Test]
    public void TwoFeedsOnOneInstrument_CanCarryDifferentProducts()
    {
        var eobi = Publish(FeedProducts.ByOrder | FeedProducts.Status);
        var emdi = Publish(FeedProducts.ByPrice | FeedProducts.Trades | FeedProducts.Status);

        Assert.AreEqual(new[] {nameof(MarketByOrderDeltaEvent)}, Kinds(eobi.Incremental).ToArray());
        Assert.AreEqual(new[] {nameof(TradeDataEvent), nameof(MarketByPriceDeltaEvent)},
            Kinds(emdi.Incremental).ToArray());

        Assert.Contains(nameof(InstrumentStatusDataEvent), Kinds(eobi.Snapshot).ToArray());
        Assert.Contains(nameof(InstrumentStatusDataEvent), Kinds(emdi.Snapshot).ToArray(),
            "state is on both, because a subscriber to either needs to know the instrument is open");
    }

    [Test]
    public void AFeedCarryingNothing_IsRefused()
    {
        Assert.Throws<ArgumentException>(() => new InstrumentFeed(Gold.Symbol, FeedProducts.None));
    }

    [Test]
    public void AFeed_SaysWhatItCarries()
    {
        var feed = new InstrumentFeed(Gold.Symbol, FeedProducts.ByPrice | FeedProducts.Trades);

        Assert.AreEqual(FeedProducts.ByPrice | FeedProducts.Trades, feed.Products);
    }
}
