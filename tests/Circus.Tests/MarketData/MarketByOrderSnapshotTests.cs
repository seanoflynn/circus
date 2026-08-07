using Circus.Actions;
using Circus.MarketData;
using Circus.Tests.Helpers;
using NUnit.Framework;

namespace Circus.Tests.MarketData;

// The by-order half of the snapshot feed, and the pairing that makes its incremental half
// reconstructible. Between them, a subscriber can rebuild an order-by-order book from a stream it
// joined late and then keep it in step.
public class MarketByOrderSnapshotTests
{
    private static readonly Instrument Gold = new("GCZ6", 10, 10);

    private static readonly DateTime Now1 = new(2000, 1, 1, 12, 0, 0);
    private static readonly DateTime Now2 = new(2000, 1, 1, 12, 1, 0);
    private static readonly DateTime Now3 = new(2000, 1, 1, 12, 2, 0);
    private static readonly DateTime Now4 = new(2000, 1, 1, 12, 3, 0);

    private OrderBook _book = null!;

    [SetUp]
    public void SetUp() => _book = new OrderBook(Gold);

    private OrdersDataEvent Snapshot(DateTime time) =>
        ProductFeed.Carrying(FeedProducts.ByOrder)
            .PublishImage<OrdersDataEvent>(_book.Process(new PublishSnapshot {Symbol = Gold.Symbol, Time = time}))
            .Single();

    [Test]
    public void AnEmptyBook_SnapshotsNoOrders()
    {
        _book.UpdateStatus(OrderBookStatus.Open, time: Now1);

        Assert.IsEmpty(Snapshot(Now2).Orders);
    }

    // Best price outward, and within a price in the order the book would match them - a consumer
    // replaying these arrives holding the queue the book actually has, which is the whole point of
    // an order-by-order product.
    [Test]
    public void Orders_ComeOutBestPriceOutwardAndInQueueOrder()
    {
        _book.UpdateStatus(OrderBookStatus.Open, time: Now1);
        _book.CreateLimitOrder("C1", "O1", new OrderValidity.Day(), Side.Buy, 3, 100, time: Now2);
        _book.CreateLimitOrder("C2", "O2", new OrderValidity.Day(), Side.Buy, 4, 100, time: Now3);
        _book.CreateLimitOrder("C3", "O3", new OrderValidity.Day(), Side.Buy, 5, 110, time: Now3);
        _book.CreateLimitOrder("C4", "O4", new OrderValidity.Day(), Side.Sell, 6, 200, time: Now3);

        var orders = Snapshot(Now4).Orders;

        Assert.AreEqual(new[] {110m, 100m, 100m, 200m}, orders.Select(o => o.Price).ToArray(),
            "bids best-first, then offers");
        Assert.AreEqual(new[] {5, 3, 4, 6}, orders.Select(o => o.Quantity).ToArray(),
            "and within a price, the one that rested first comes first");
        Assert.AreEqual(new[] {Side.Buy, Side.Buy, Side.Buy, Side.Sell},
            orders.Select(o => o.Side).ToArray());
    }

    [Test]
    public void AnIceberg_SnapshotsItsPeakOnly()
    {
        _book.UpdateStatus(OrderBookStatus.Open, time: Now1);
        _book.CreateLimitOrder("C1", "O1", new OrderValidity.Day(), Side.Sell, 20, 100,
            maxVisibleQuantity: 5, time: Now2);

        var orders = Snapshot(Now3).Orders;

        Assert.AreEqual(1, orders.Count);
        Assert.AreEqual(5, orders[0].Quantity, "the peak, never the hidden reserve");
    }

    [Test]
    public void AnUntriggeredStop_IsNotInTheWorkingBook()
    {
        _book.UpdateStatus(OrderBookStatus.Open, time: Now1);
        _book.CreateLimitOrder("C1", "O1", new OrderValidity.Day(), Side.Buy, 3, 500, time: Now2);
        _book.CreateLimitOrder("C2", "O2", new OrderValidity.Day(), Side.Sell, 3, 500, time: Now2);
        _book.CreateStopLimitOrder("C3", "O3", new OrderValidity.Day(), Side.Buy, 5, 530, 510,
            time: Now3);

        Assert.IsEmpty(Snapshot(Now4).Orders,
            "a stop that has not triggered rests in a different ladder and is not displayed");
    }

    [Test]
    public void ASnapshot_CarriesNoClientIdentity()
    {
        var properties = typeof(RestingOrder).GetProperties().Select(p => p.Name).ToList();

        Assert.IsFalse(properties.Contains("CompanyId"),
            "a public feed must never carry the originating client's CompanyId");
        Assert.IsFalse(properties.Contains("ClientOrderId"),
            "a public feed must never carry the originating client's ClientOrderId");
    }

    // The gap this phase closes on the incremental side. A trade is two entries, one per side, and
    // without the shared id a consumer cannot tell one trade between two orders from two separate
    // trades at the same price.
    [Test]
    public void AFill_CarriesTheTradeIdThatPairsItsTwoSides()
    {
        var feed = ProductFeed.Carrying(FeedProducts.ByOrder);
        _book.UpdateStatus(OrderBookStatus.Open, time: Now1);
        _book.CreateLimitOrder("C1", "O1", new OrderValidity.Day(), Side.Buy, 3, 100, time: Now2);

        var changes = feed.Publish<MarketByOrderDeltaEvent>(
                _book.CreateLimitOrder("C2", "O2", new OrderValidity.Day(), Side.Sell, 3, 100, time: Now3))
            .Single().Changes;

        var fills = changes.Where(c => c.Action == MarketByOrderDeltaAction.Filled).ToList();
        Assert.AreEqual(2, fills.Count, "one trade, one entry per side");
        Assert.IsNotNull(fills[0].TradeId);
        Assert.AreEqual(fills[0].TradeId, fills[1].TradeId, "the two sides of one trade share an id");
        Assert.AreNotEqual(fills[0].ExchangeOrderId, fills[1].ExchangeOrderId);
    }

    [Test]
    public void TwoTradesInOneAction_AreToldApartByTheirIds()
    {
        var feed = ProductFeed.Carrying(FeedProducts.ByOrder);
        _book.UpdateStatus(OrderBookStatus.Open, time: Now1);
        _book.CreateLimitOrder("C1", "O1", new OrderValidity.Day(), Side.Sell, 2, 100, time: Now2);
        _book.CreateLimitOrder("C2", "O2", new OrderValidity.Day(), Side.Sell, 3, 110, time: Now2);

        var changes = feed.Publish<MarketByOrderDeltaEvent>(
                _book.CreateLimitOrder("C3", "O3", new OrderValidity.Day(), Side.Buy, 5, 110, time: Now3))
            .Single().Changes;

        var tradeIds = changes.Where(c => c.Action == MarketByOrderDeltaAction.Filled)
            .Select(c => c.TradeId).ToList();

        Assert.AreEqual(4, tradeIds.Count, "two trades, two sides each");
        Assert.AreEqual(2, tradeIds.Distinct().Count(),
            "and they group into two trades rather than one run of fills");
    }

    [Test]
    public void EverythingButAFill_CarriesNoTradeId()
    {
        var feed = ProductFeed.Carrying(FeedProducts.ByOrder);
        _book.UpdateStatus(OrderBookStatus.Open, time: Now1);

        var changes = feed.Publish<MarketByOrderDeltaEvent>(
                _book.CreateLimitOrder("C1", "O1", new OrderValidity.Day(), Side.Buy, 3, 100, time: Now2))
            .Single().Changes;

        Assert.IsTrue(changes.All(c => c.TradeId == null),
            "a resting order is not part of a trade and must not claim to be");
    }
}
