using Circus.Events;
using Circus.MarketData;
using NUnit.Framework;

namespace Circus.Tests.MarketData;

// OrderBook.GetLevels reads the aggregate the price ladders maintain as orders rest, fill and
// leave, rather than deriving it from the event stream the way LevelDataProducer does.
//
// Internal rather than public, and asserted here directly, because it is how the book will build
// the image a snapshot tick asks it for - not a seam a consumer reaches through. Process stays
// the only way anything outside learns what a book is holding.
//
// The two are independent implementations of the same answer, so most of this file is written as
// a differential: drive a scenario, then assert the book and the producer agree. That is worth
// more than either set of expected values on its own - a shared misunderstanding would have to be
// made twice, in two different styles, to slip through. The direct assertions that follow pin the
// cases where agreeing on the wrong answer is conceivable.
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

    private const int Deep = 100;

    private OrderBook _book = null!;
    private LevelDataProducer _producer = null!;

    [SetUp]
    public void SetUp()
    {
        _book = new OrderBook(Gold);
        _producer = new LevelDataProducer(Deep);
    }

    // Every action goes through both, in order - the producer can never be fed a later action's
    // events alone, since it rebuilds its state from all of them.
    private LevelsDataEvent Drive(IReadOnlyList<OrderBookEvent> events)
    {
        var produced = _producer.Process(events);
        Assert.AreEqual(1, produced.Count);
        return produced[0];
    }

    private void AssertBookAgreesWithProducer(LevelsDataEvent derived)
    {
        Assert.AreEqual(derived.Bids, _book.GetLevels(Side.Buy, Deep),
            "book-held aggregate disagrees with the levels derived from the event stream (bids)");
        Assert.AreEqual(derived.Offers, _book.GetLevels(Side.Sell, Deep),
            "book-held aggregate disagrees with the levels derived from the event stream (offers)");
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
        var derived = Drive(_book.CreateLimitOrder("C2", "O2", new OrderValidity.Day(), Side.Buy, 4, 100,
            time: Now3));

        var bids = _book.GetLevels(Side.Buy, Deep);
        Assert.AreEqual(1, bids.Count);
        Assert.AreEqual(100, bids[0].Price);
        Assert.AreEqual(7, bids[0].Quantity, "both orders' displayed size");
        Assert.AreEqual(2, bids[0].Count);
        AssertBookAgreesWithProducer(derived);
    }

    [Test]
    public void Levels_AreOrderedBestFirst_OnBothSides()
    {
        Drive(_book.UpdateStatus(OrderBookStatus.Open, time: Now1));
        Drive(_book.CreateLimitOrder("C1", "O1", new OrderValidity.Day(), Side.Buy, 1, 100, time: Now2));
        Drive(_book.CreateLimitOrder("C2", "O2", new OrderValidity.Day(), Side.Buy, 1, 120, time: Now2));
        Drive(_book.CreateLimitOrder("C3", "O3", new OrderValidity.Day(), Side.Sell, 1, 200, time: Now3));
        var derived = Drive(_book.CreateLimitOrder("C4", "O4", new OrderValidity.Day(), Side.Sell, 1, 180,
            time: Now3));

        Assert.AreEqual(new[] {120m, 100m}, _book.GetLevels(Side.Buy, Deep).Select(l => l.Price).ToArray(),
            "bids run from the highest price outward");
        Assert.AreEqual(new[] {180m, 200m}, _book.GetLevels(Side.Sell, Deep).Select(l => l.Price).ToArray(),
            "offers run from the lowest price outward");
        AssertBookAgreesWithProducer(derived);
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
        var derived = Drive(_book.CancelOrder("C1", "O1b", "O1", time: Now3));

        Assert.IsEmpty(_book.GetLevels(Side.Buy, Deep));
        AssertBookAgreesWithProducer(derived);
    }

    [Test]
    public void QuantityDecrease_AdjustsTheLevelInPlace()
    {
        Drive(_book.UpdateStatus(OrderBookStatus.Open, time: Now1));
        Drive(_book.CreateLimitOrder("C1", "O1", new OrderValidity.Day(), Side.Buy, 10, 100, time: Now2));
        var derived = Drive(_book.UpdateOrder("C1", "O1b", "O1", 4, 100, time: Now3));

        var bids = _book.GetLevels(Side.Buy, Deep);
        Assert.AreEqual(4, bids[0].Quantity);
        Assert.AreEqual(1, bids[0].Count);
        AssertBookAgreesWithProducer(derived);
    }

    [Test]
    public void Reprice_MovesQuantityBetweenLevels()
    {
        Drive(_book.UpdateStatus(OrderBookStatus.Open, time: Now1));
        Drive(_book.CreateLimitOrder("C1", "O1", new OrderValidity.Day(), Side.Buy, 6, 100, time: Now2));
        var derived = Drive(_book.UpdateOrder("C1", "O1b", "O1", 6, 90, time: Now3));

        var bids = _book.GetLevels(Side.Buy, Deep);
        Assert.AreEqual(1, bids.Count, "the level it left is gone, not left behind empty");
        Assert.AreEqual(90, bids[0].Price);
        Assert.AreEqual(6, bids[0].Quantity);
        AssertBookAgreesWithProducer(derived);
    }

    [Test]
    public void Fill_ReducesTheRestingLevel()
    {
        Drive(_book.UpdateStatus(OrderBookStatus.Open, time: Now1));
        Drive(_book.CreateLimitOrder("C1", "O1", new OrderValidity.Day(), Side.Buy, 10, 100, time: Now2));
        var derived = Drive(_book.CreateLimitOrder("C2", "O2", new OrderValidity.Day(), Side.Sell, 4, 100,
            time: Now3));

        var bids = _book.GetLevels(Side.Buy, Deep);
        Assert.AreEqual(6, bids[0].Quantity, "what is left resting after the trade");
        Assert.IsEmpty(_book.GetLevels(Side.Sell, Deep), "the aggressor filled and never rested");
        AssertBookAgreesWithProducer(derived);
    }

    [Test]
    public void Iceberg_ShowsOnlyTheDisplayedPeak()
    {
        Drive(_book.UpdateStatus(OrderBookStatus.Open, time: Now1));
        var derived = Drive(_book.CreateLimitOrder("C1", "O1", new OrderValidity.Day(), Side.Sell, 20, 100,
            maxVisibleQuantity: 5, time: Now2));

        var offers = _book.GetLevels(Side.Sell, Deep);
        Assert.AreEqual(5, offers[0].Quantity, "the peak, never the hidden reserve");
        AssertBookAgreesWithProducer(derived);
    }

    [Test]
    public void IcebergPeakExhaustedInContinuousTrading_LevelShowsTheRefreshedPeak()
    {
        Drive(_book.UpdateStatus(OrderBookStatus.Open, time: Now1));
        Drive(_book.CreateLimitOrder("C1", "O1", new OrderValidity.Day(), Side.Sell, 20, 100,
            maxVisibleQuantity: 5, time: Now2));

        // Takes the whole peak, so the order requeues showing a fresh one.
        var derived = Drive(_book.CreateLimitOrder("C2", "O2", new OrderValidity.Day(), Side.Buy, 5, 100,
            time: Now3));

        var offers = _book.GetLevels(Side.Sell, Deep);
        Assert.AreEqual(1, offers.Count);
        Assert.AreEqual(5, offers[0].Quantity, "a fresh peak from the reserve, not an emptied level");
        Assert.AreEqual(1, offers[0].Count);
        AssertBookAgreesWithProducer(derived);
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

        var derived = Drive(_book.UpdateStatus(OrderBookStatus.Open, time: Now4));

        var bids = _book.GetLevels(Side.Buy, Deep);
        Assert.IsEmpty(_book.GetLevels(Side.Sell, Deep));
        Assert.AreEqual(1, bids.Count);
        Assert.AreEqual(10, bids[0].Quantity,
            "a fresh peak, not the traded quantity taken off the peak it was showing");
        Assert.AreEqual(1, bids[0].Count);
        AssertBookAgreesWithProducer(derived);
    }

    [Test]
    public void UntriggeredStop_DoesNotAppearInTheWorkingBook()
    {
        Drive(_book.UpdateStatus(OrderBookStatus.Open, time: Now1));
        Drive(_book.CreateLimitOrder("C1", "O1", new OrderValidity.Day(), Side.Buy, 3, 500, time: Now2));
        Drive(_book.CreateLimitOrder("C2", "O2", new OrderValidity.Day(), Side.Sell, 3, 500, time: Now2));

        var derived = Drive(_book.CreateStopLimitOrder("C3", "O3", new OrderValidity.Day(), Side.Buy, 5, 530, 510,
            time: Now3));

        Assert.IsFalse(_book.GetLevels(Side.Buy, Deep).Any(l => l.Price == 530),
            "an untriggered stop rests in the stops ladder and is not on the working book");
        AssertBookAgreesWithProducer(derived);
    }
}
