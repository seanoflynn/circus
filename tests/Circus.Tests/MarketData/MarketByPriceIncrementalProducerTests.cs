using Circus.Events;
using Circus.MarketData;
using Circus.Tests.Helpers;
using NUnit.Framework;

namespace Circus.Tests.MarketData;

// The producer translates the book's LevelsChanged into the message a venue publishes and holds
// nothing, so these are about the shape of what a subscriber receives - one message per book
// update, carrying every level it moved. That the numbers in it are right is BookLevelViewTests'
// subject, which drives the whole path and compares the ends.
public class MarketByPriceIncrementalProducerTests
{
    private static readonly Instrument Gold = new("GCZ6", 10, 10);

    private static readonly DateTime Now1 = new(2000, 1, 1, 12, 0, 0);
    private static readonly DateTime Now2 = new(2000, 1, 1, 12, 1, 0);
    private static readonly DateTime Now3 = new(2000, 1, 1, 12, 2, 0);

    private OrderBook _book = null!;
    private MarketByPriceIncrementalProducer _producer = null!;

    [SetUp]
    public void SetUp()
    {
        _book = new OrderBook(Gold);
        _producer = new MarketByPriceIncrementalProducer();
    }

    [Test]
    public void AStatusChange_ProducesNoDepth()
    {
        Assert.IsEmpty(_producer.Process(_book.UpdateStatus(OrderBookStatus.Open, time: Now1)));
    }

    [Test]
    public void ARestingOrder_AddsItsLevel()
    {
        _producer.Process(_book.UpdateStatus(OrderBookStatus.Open, time: Now1));

        var messages = _producer.Process(
            _book.CreateLimitOrder("C1", "O1", new OrderValidity.Day(), Side.Buy, 3, 100, time: Now2));

        Assert.AreEqual(1, messages.Count, "one action, one book update");
        Assert.AreEqual(Now2, messages[0].Time);
        Assert.AreEqual(Gold.Symbol, messages[0].Symbol);

        var changes = messages[0].Changes;
        Assert.AreEqual(1, changes.Count);
        Assert.AreEqual(MarketByPriceDeltaAction.Added, changes[0].Action);
        Assert.AreEqual(Side.Buy, changes[0].Side);
        Assert.AreEqual(100, changes[0].Price);
        Assert.AreEqual(3, changes[0].Quantity);
        Assert.AreEqual(1, changes[0].Count);
        Assert.AreEqual(1, changes[0].LevelIndex, "the only level, so the top of the book");
    }

    [Test]
    public void ASecondOrderAtOnePrice_ModifiesThatLevel()
    {
        _producer.Process(_book.UpdateStatus(OrderBookStatus.Open, time: Now1));
        _producer.Process(_book.CreateLimitOrder("C1", "O1", new OrderValidity.Day(), Side.Buy, 3, 100,
            time: Now2));

        var changes = _producer.Process(
            _book.CreateLimitOrder("C2", "O2", new OrderValidity.Day(), Side.Buy, 4, 100, time: Now3))
            .Single().Changes;

        Assert.AreEqual(1, changes.Count, "one level moved");
        Assert.AreEqual(MarketByPriceDeltaAction.Modified, changes[0].Action);
        Assert.AreEqual(7, changes[0].Quantity);
        Assert.AreEqual(2, changes[0].Count);
    }

    [Test]
    public void AnEmptiedLevel_IsRemovedAndCarriesNothing()
    {
        _producer.Process(_book.UpdateStatus(OrderBookStatus.Open, time: Now1));
        _producer.Process(_book.CreateLimitOrder("C1", "O1", new OrderValidity.Day(), Side.Buy, 3, 100,
            time: Now2));

        var changes = _producer.Process(_book.CancelOrder("C1", "O1b", "O1", time: Now3))
            .Single().Changes;

        Assert.AreEqual(1, changes.Count);
        Assert.AreEqual(MarketByPriceDeltaAction.Removed, changes[0].Action);
        Assert.AreEqual(100, changes[0].Price, "the price identifies which level left");
        Assert.AreEqual(0, changes[0].Quantity);
        Assert.AreEqual(0, changes[0].Count);
    }

    // The reason for keying on price: a better bid arriving does not restate the levels beneath
    // it, where a positional feed would have to shift every one of them down a rung.
    [Test]
    public void ABetterPriceArriving_DoesNotRestateTheLevelsBeneathIt()
    {
        _producer.Process(_book.UpdateStatus(OrderBookStatus.Open, time: Now1));
        _producer.Process(_book.CreateLimitOrder("C1", "O1", new OrderValidity.Day(), Side.Buy, 3, 100,
            time: Now2));

        var changes = _producer.Process(
            _book.CreateLimitOrder("C2", "O2", new OrderValidity.Day(), Side.Buy, 4, 110, time: Now3))
            .Single().Changes;

        Assert.AreEqual(1, changes.Count, "only the new level is news; the one below it is unchanged");
        Assert.AreEqual(110, changes[0].Price);
        Assert.AreEqual(MarketByPriceDeltaAction.Added, changes[0].Action);
    }

    // One action, several levels, one message: an aggressor sweeping the book is a single update
    // carrying both levels it emptied, not two updates and not one per fill along the way.
    [Test]
    public void AnAggressorSweepingTwoLevels_IsOneMessageCarryingBoth()
    {
        _producer.Process(_book.UpdateStatus(OrderBookStatus.Open, time: Now1));
        _producer.Process(_book.CreateLimitOrder("C1", "O1", new OrderValidity.Day(), Side.Sell, 2, 100,
            time: Now2));
        _producer.Process(_book.CreateLimitOrder("C2", "O2", new OrderValidity.Day(), Side.Sell, 2, 110,
            time: Now2));

        var messages = _producer.Process(
            _book.CreateLimitOrder("C3", "O3", new OrderValidity.Day(), Side.Buy, 4, 110, time: Now3));

        Assert.AreEqual(1, messages.Count, "one book update, however many levels it moved");

        var offers = messages[0].Changes.Where(d => d.Side == Side.Sell).ToList();
        Assert.AreEqual(2, offers.Count, "one entry per level swept, not one per fill");
        Assert.IsTrue(offers.All(d => d.Action == MarketByPriceDeltaAction.Removed));
        Assert.AreEqual(new[] {100m, 110m}, offers.Select(d => d.Price).OrderBy(p => p).ToArray());
        Assert.IsEmpty(messages[0].Changes.Where(d => d.Side == Side.Buy),
            "the aggressor filled and never rested");
    }

    [Test]
    public void AnIceberg_PublishesOnlyItsPeak()
    {
        _producer.Process(_book.UpdateStatus(OrderBookStatus.Open, time: Now1));

        var changes = _producer.Process(_book.CreateLimitOrder("C1", "O1", new OrderValidity.Day(),
            Side.Sell, 20, 100, maxVisibleQuantity: 5, time: Now2)).Single().Changes;

        Assert.AreEqual(1, changes.Count);
        Assert.AreEqual(5, changes[0].Quantity, "the peak, never the hidden reserve");
    }
}
