using Circus.Events;
using Circus.MarketData;
using NUnit.Framework;

namespace Circus.Tests.MarketData;

// OrderBook.GetLevels reads the aggregate the price ladders maintain as orders rest, fill and
// leave. It is internal because it is how the book will build the image a snapshot tick asks it
// for, not a seam a consumer reaches through: Process stays the only way anything outside learns
// what a book is holding.
//
// The book publishes that same aggregate as LevelsChanged, the by-price feed turns that into
// deltas, and a subscriber applies them to a LevelBook of its own. So most of this file is a
// differential across that whole path: drive a scenario, then assert the book's own view and a
// subscriber's rebuilt one agree. That is worth more than either set of expected values alone -
// the aggregate, the diffing that turns it into deltas, and the applying that turns deltas back
// into a ladder would all have to be wrong compatibly to slip through. The direct assertions
// alongside pin the cases where agreeing on the wrong answer is conceivable.
//
// A bare OrderBook rather than a TimestampingOrderBook: the level view is on the book itself, and
// these tests stamp their own times anyway.
public class BookLevelViewTests
{
    private static readonly Instrument Gold = new("GCZ6", 10, 10);

    private static readonly DateTime Now1 = new(2000, 1, 1, 12, 0, 0);
    private static readonly DateTime Now2 = new(2000, 1, 1, 12, 1, 0);
    private static readonly DateTime Now3 = new(2000, 1, 1, 12, 2, 0);
    private static readonly DateTime Now4 = new(2000, 1, 1, 12, 3, 0);

    // Deeper than the book publishes, for the assertions that are about what it is holding rather
    // than about what a subscriber was told.
    private const int Deep = 100;

    // What the feed actually carries, and so the most a subscriber can be held to.
    private const int PublishedDepth = 10;

    private OrderBook _book = null!;
    private MarketByPriceIncrementalProducer _producer = null!;
    private LevelBook _subscriber = null!;

    [SetUp]
    public void SetUp()
    {
        _book = new OrderBook(Gold);
        _producer = new MarketByPriceIncrementalProducer();
        _subscriber = new LevelBook();
    }

    // Every action's events go through the feed, in order - a subscriber that skips a batch has
    // missed messages, which is exactly what it cannot recover from until snapshots exist.
    private void Drive(IReadOnlyList<OrderBookEvent> events)
    {
        foreach (var delta in _producer.Process(events))
            _subscriber.Apply(delta);
    }

    // Compared at the published depth, not at Deep: a subscriber is only ever told about the ten
    // levels the feed carries, so holding it to anything beyond that would be asserting it knows
    // something nobody sent it.
    private void AssertSubscriberAgrees()
    {
        Assert.AreEqual(_book.GetLevels(Side.Buy, PublishedDepth), _subscriber.Bids,
            "a subscriber's rebuilt ladder disagrees with the book's own aggregate (bids)");
        Assert.AreEqual(_book.GetLevels(Side.Sell, PublishedDepth), _subscriber.Offers,
            "a subscriber's rebuilt ladder disagrees with the book's own aggregate (offers)");
    }

    [Test]
    public void EmptyBook_HasNoLevels()
    {
        Drive(_book.UpdateStatus(OrderBookStatus.Open, time: Now1));

        Assert.IsEmpty(_book.GetLevels(Side.Buy, Deep));
        Assert.IsEmpty(_book.GetLevels(Side.Sell, Deep));
    }

    [Test]
    public void OrdersAtOnePrice_AggregateIntoOneLevel()
    {
        Drive(_book.UpdateStatus(OrderBookStatus.Open, time: Now1));
        Drive(_book.CreateLimitOrder("C1", "O1", new OrderValidity.Day(), Side.Buy, 3, 100, time: Now2));
        Drive(_book.CreateLimitOrder("C2", "O2", new OrderValidity.Day(), Side.Buy, 4, 100,
            time: Now3));

        var bids = _book.GetLevels(Side.Buy, Deep);
        Assert.AreEqual(1, bids.Count);
        Assert.AreEqual(100, bids[0].Price);
        Assert.AreEqual(7, bids[0].Quantity, "both orders' displayed size");
        Assert.AreEqual(2, bids[0].Count);
        AssertSubscriberAgrees();
    }

    [Test]
    public void Levels_AreOrderedBestFirst_OnBothSides()
    {
        Drive(_book.UpdateStatus(OrderBookStatus.Open, time: Now1));
        Drive(_book.CreateLimitOrder("C1", "O1", new OrderValidity.Day(), Side.Buy, 1, 100, time: Now2));
        Drive(_book.CreateLimitOrder("C2", "O2", new OrderValidity.Day(), Side.Buy, 1, 120, time: Now2));
        Drive(_book.CreateLimitOrder("C3", "O3", new OrderValidity.Day(), Side.Sell, 1, 200, time: Now3));
        Drive(_book.CreateLimitOrder("C4", "O4", new OrderValidity.Day(), Side.Sell, 1, 180,
            time: Now3));

        Assert.AreEqual(new[] {120m, 100m}, _book.GetLevels(Side.Buy, Deep).Select(l => l.Price).ToArray(),
            "bids run from the highest price outward");
        Assert.AreEqual(new[] {180m, 200m}, _book.GetLevels(Side.Sell, Deep).Select(l => l.Price).ToArray(),
            "offers run from the lowest price outward");
        AssertSubscriberAgrees();
    }

    [Test]
    public void MaxLevels_CapsFromTheBestOutward()
    {
        Drive(_book.UpdateStatus(OrderBookStatus.Open, time: Now1));
        for (var i = 0; i < 5; i++)
            Drive(_book.CreateLimitOrder($"C{i}", $"O{i}", new OrderValidity.Day(), Side.Buy, 1, 100 - i * 10,
                time: Now2));

        Assert.AreEqual(new[] {100m, 90m}, _book.GetLevels(Side.Buy, 2).Select(l => l.Price).ToArray());
        Assert.AreEqual(5, _book.GetLevels(Side.Buy, Deep).Count);
        Assert.IsEmpty(_book.GetLevels(Side.Buy, 0));
    }

    [Test]
    public void Cancel_RemovesTheLevelWhenItsLastOrderLeaves()
    {
        Drive(_book.UpdateStatus(OrderBookStatus.Open, time: Now1));
        Drive(_book.CreateLimitOrder("C1", "O1", new OrderValidity.Day(), Side.Buy, 3, 100, time: Now2));
        Drive(_book.CancelOrder("C1", "O1b", "O1", time: Now3));

        Assert.IsEmpty(_book.GetLevels(Side.Buy, Deep));
        AssertSubscriberAgrees();
    }

    [Test]
    public void QuantityDecrease_AdjustsTheLevelInPlace()
    {
        Drive(_book.UpdateStatus(OrderBookStatus.Open, time: Now1));
        Drive(_book.CreateLimitOrder("C1", "O1", new OrderValidity.Day(), Side.Buy, 10, 100, time: Now2));
        Drive(_book.UpdateOrder("C1", "O1b", "O1", 4, 100, time: Now3));

        var bids = _book.GetLevels(Side.Buy, Deep);
        Assert.AreEqual(4, bids[0].Quantity);
        Assert.AreEqual(1, bids[0].Count);
        AssertSubscriberAgrees();
    }

    [Test]
    public void Reprice_MovesQuantityBetweenLevels()
    {
        Drive(_book.UpdateStatus(OrderBookStatus.Open, time: Now1));
        Drive(_book.CreateLimitOrder("C1", "O1", new OrderValidity.Day(), Side.Buy, 6, 100, time: Now2));
        Drive(_book.UpdateOrder("C1", "O1b", "O1", 6, 90, time: Now3));

        var bids = _book.GetLevels(Side.Buy, Deep);
        Assert.AreEqual(1, bids.Count, "the level it left is gone, not left behind empty");
        Assert.AreEqual(90, bids[0].Price);
        Assert.AreEqual(6, bids[0].Quantity);
        AssertSubscriberAgrees();
    }

    [Test]
    public void Fill_ReducesTheRestingLevel()
    {
        Drive(_book.UpdateStatus(OrderBookStatus.Open, time: Now1));
        Drive(_book.CreateLimitOrder("C1", "O1", new OrderValidity.Day(), Side.Buy, 10, 100, time: Now2));
        Drive(_book.CreateLimitOrder("C2", "O2", new OrderValidity.Day(), Side.Sell, 4, 100,
            time: Now3));

        var bids = _book.GetLevels(Side.Buy, Deep);
        Assert.AreEqual(6, bids[0].Quantity, "what is left resting after the trade");
        Assert.IsEmpty(_book.GetLevels(Side.Sell, Deep), "the aggressor filled and never rested");
        AssertSubscriberAgrees();
    }

    [Test]
    public void Iceberg_ShowsOnlyTheDisplayedPeak()
    {
        Drive(_book.UpdateStatus(OrderBookStatus.Open, time: Now1));
        Drive(_book.CreateLimitOrder("C1", "O1", new OrderValidity.Day(), Side.Sell, 20, 100,
            maxVisibleQuantity: 5, time: Now2));

        var offers = _book.GetLevels(Side.Sell, Deep);
        Assert.AreEqual(5, offers[0].Quantity, "the peak, never the hidden reserve");
        AssertSubscriberAgrees();
    }

    [Test]
    public void IcebergPeakExhaustedInContinuousTrading_LevelShowsTheRefreshedPeak()
    {
        Drive(_book.UpdateStatus(OrderBookStatus.Open, time: Now1));
        Drive(_book.CreateLimitOrder("C1", "O1", new OrderValidity.Day(), Side.Sell, 20, 100,
            maxVisibleQuantity: 5, time: Now2));

        // Takes the whole peak, so the order requeues showing a fresh one.
        Drive(_book.CreateLimitOrder("C2", "O2", new OrderValidity.Day(), Side.Buy, 5, 100,
            time: Now3));

        var offers = _book.GetLevels(Side.Sell, Deep);
        Assert.AreEqual(1, offers.Count);
        Assert.AreEqual(5, offers[0].Quantity, "a fresh peak from the reserve, not an emptied level");
        Assert.AreEqual(1, offers[0].Count);
        AssertSubscriberAgrees();
    }

    // The case the running aggregate has to get right without being told: an auction sizes its
    // print off full remaining quantity, so it trades straight through the peak into the reserve
    // and leaves the order displaying a fresh peak - with no requeue event, because the peak never
    // reached zero. The level has to follow the order's displayed size rather than the quantity
    // that traded, and here it does so by construction.
    [Test]
    public void AuctionPrintThroughIcebergPeak_LevelShowsTheRefreshedPeak()
    {
        Drive(_book.UpdateStatus(OrderBookStatus.PreOpen, time: Now1));
        Drive(_book.CreateLimitOrder("C1", "O1", new OrderValidity.Day(), Side.Buy, 100, 100,
            maxVisibleQuantity: 10, time: Now2));
        Drive(_book.CreateLimitOrder("C2", "O2", new OrderValidity.Day(), Side.Sell, 45, 100, time: Now3));

        Drive(_book.UpdateStatus(OrderBookStatus.Open, time: Now4));

        var bids = _book.GetLevels(Side.Buy, Deep);
        Assert.IsEmpty(_book.GetLevels(Side.Sell, Deep));
        Assert.AreEqual(1, bids.Count);
        Assert.AreEqual(10, bids[0].Quantity,
            "a fresh peak, not the traded quantity taken off the peak it was showing");
        Assert.AreEqual(1, bids[0].Count);
        AssertSubscriberAgrees();
    }

    [Test]
    public void UntriggeredStop_DoesNotAppearInTheWorkingBook()
    {
        Drive(_book.UpdateStatus(OrderBookStatus.Open, time: Now1));
        Drive(_book.CreateLimitOrder("C1", "O1", new OrderValidity.Day(), Side.Buy, 3, 500, time: Now2));
        Drive(_book.CreateLimitOrder("C2", "O2", new OrderValidity.Day(), Side.Sell, 3, 500, time: Now2));

        Drive(_book.CreateStopLimitOrder("C3", "O3", new OrderValidity.Day(), Side.Buy, 5, 530, 510,
            time: Now3));

        Assert.IsFalse(_book.GetLevels(Side.Buy, Deep).Any(l => l.Price == 530),
            "an untriggered stop rests in the stops ladder and is not on the working book");
        AssertSubscriberAgrees();
    }
}
