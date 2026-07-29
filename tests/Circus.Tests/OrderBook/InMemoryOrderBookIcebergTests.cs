using Circus.OrderBook;
using Circus.OrderBook.Actions;
using Circus.OrderBook.Events;
using Circus.TimeProviders;
using NUnit.Framework;

namespace Circus.Tests.OrderBook;

[TestFixture]
public class InMemoryOrderBookIcebergTests
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
    private static readonly string OrderId1B = "Order1b";

    private static TestTimeProvider TimeProvider;
    private static LevelTrackingOrderBook Book;

    [SetUp]
    public void SetUp()
    {
        TimeProvider = new TestTimeProvider(Now1);
        Book = new LevelTrackingOrderBook(Sec, TimeProvider);
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(6)]
    public void MaxVisibleQuantity_OutsideValidRange_Rejected(int maxVisibleQuantity)
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);

        // act
        var events = Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 5, 100,
            maxVisibleQuantity: maxVisibleQuantity);

        // assert
        var rejected = events[0] as CreateOrderRejected;
        Assert.IsNotNull(rejected);
        Assert.AreEqual(OrderRejectedReason.MaxVisibleQuantityOutOfRange, rejected.Reason);
    }

    [Test]
    public void Levels_ReportDisplayedPeak_NotTrueRemainingSize()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);

        // act - total 20, only 5 displayed at a time
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 20, 100,
            maxVisibleQuantity: 5);

        // assert
        var levels = Book.GetLevels(Side.Sell, 10);
        Assert.AreEqual(1, levels.Count);
        Assert.AreEqual(5, levels[0].Quantity);
        Assert.AreEqual(1, levels[0].Count);
    }

    [Test]
    public void Replenishment_LosesPriority_BystanderMatchedBeforeReplenishedPeak()
    {
        // arrange - iceberg (peak 5, total 12) rests first, a plain order arrives right
        // behind it at the same price
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 12, 100,
            maxVisibleQuantity: 5);
        TimeProvider.SetCurrentTime(Now2);
        Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Sell, 5, 100);
        TimeProvider.SetCurrentTime(Now3);

        // act - an aggressor larger than the peak
        var events = Book.CreateLimitOrder(CompanyId3, OrderId3, new OrderValidity.Day(), Side.Buy, 8, 100);

        // assert - first fill consumes the iceberg's full peak (5); it still has 7 left, so it
        // replenishes (to a full peak of 5 again) and is requeued behind Company2 - visible in
        // the feed as its own UpdateOrderConfirmed, with a fresh ExchangeOrderId marking the
        // lost priority
        Assert.AreEqual(4, events.Count);
        Assert.IsInstanceOf<CreateOrderConfirmed>(events[0]);

        var firstMatch = events[1] as OrdersMatched;
        Assert.IsNotNull(firstMatch);
        Assert.AreEqual(5, firstMatch.Quantity);
        Assert.AreEqual(OrderId1, firstMatch.Fills[0].ClientOrderId);
        Assert.AreEqual(OrderStatus.Working, firstMatch.Fills[0].Order.Status);
        Assert.AreEqual(7, firstMatch.Fills[0].Order.RemainingQuantity);
        Assert.AreEqual(0, firstMatch.Fills[0].Order.DisplayedQuantity); // not yet replenished

        var replenish = events[2] as UpdateOrderConfirmed;
        Assert.IsNotNull(replenish);
        Assert.AreEqual(OrderId1, replenish.Order.ClientOrderId);
        Assert.AreEqual(5, replenish.Order.DisplayedQuantity); // replenished back to full peak
        Assert.AreNotEqual(replenish.PreviousExchangeOrderId, replenish.Order.ExchangeOrderId);

        // third event: the aggressor's remaining 3 units match Company2 - not the iceberg's
        // freshly-replenished peak - proving the iceberg lost its queue position
        var secondMatch = events[3] as OrdersMatched;
        Assert.IsNotNull(secondMatch);
        Assert.AreEqual(3, secondMatch.Quantity);
        Assert.AreEqual(OrderId2, secondMatch.Fills[0].ClientOrderId);
        Assert.AreEqual(OrderStatus.Filled, secondMatch.Fills[1].Order.Status); // aggressor done

        // book afterward: iceberg untouched-since (7 remaining, 5 displayed), Company2 down to 2
        var sellLevels = Book.GetLevels(Side.Sell, 10);
        Assert.AreEqual(1, sellLevels.Count);
        Assert.AreEqual(2, sellLevels[0].Count);
        Assert.AreEqual(7, sellLevels[0].Quantity); // 5 (iceberg's displayed) + 2 (Company2 left)
        Assert.AreEqual(0, Book.GetLevels(Side.Buy, 10).Count);
    }

    [Test]
    public void FinalReplenishment_NotMultipleOfPeak_ShowsOnlyWhatsLeft()
    {
        // arrange - total 12, peak 5: replenishment cycle is 5, 5, then a final 2
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 12, 100,
            maxVisibleQuantity: 5);
        TimeProvider.SetCurrentTime(Now2);

        // act - a single aggressor large enough to consume the whole iceberg
        var events = Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Buy, 12, 100);

        // assert - three separate prints as the peak replenishes: 5, 5, then the 2 left over,
        // with a replenish event after each of the first two (not after the last - the
        // iceberg is fully filled at that point, nothing left to replenish)
        Assert.AreEqual(6, events.Count);
        var matches = events.OfType<OrdersMatched>().ToList();
        Assert.AreEqual(3, matches.Count);
        Assert.AreEqual(5, matches[0].Quantity);
        Assert.AreEqual(5, matches[1].Quantity);
        Assert.AreEqual(2, matches[2].Quantity);
        Assert.AreEqual(2, events.OfType<UpdateOrderConfirmed>().Count());

        var lastFill = matches[2].Fills[0];
        Assert.AreEqual(OrderId1, lastFill.ClientOrderId);
        Assert.AreEqual(OrderStatus.Filled, lastFill.Order.Status);
        Assert.AreEqual(12, lastFill.Order.FilledQuantity);

        Assert.AreEqual(0, Book.GetLevels(Side.Sell, 10).Count);
        Assert.AreEqual(0, Book.GetLevels(Side.Buy, 10).Count);
    }

    [Test]
    public void HiddenReserve_CountsInFullForMinQuantity_EvenThoughNotDisplayed()
    {
        // arrange - only 3 displayed at a time out of a true total of 20
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 20, 100,
            maxVisibleQuantity: 3);
        TimeProvider.SetCurrentTime(Now2);

        // act - requires the full 15 to fill immediately or nothing does; the published level
        // only shows 3, but the true available liquidity (20) is what the gate actually checks
        var events = Book.CreateLimitOrder(CompanyId2, OrderId2,
            new OrderValidity.ImmediateOrCancel { MinQuantity = 15 }, Side.Buy, 15, 100);

        // assert - accepted and fully filled, replenishing across several peaks (3 each) to do
        // it - the resting iceberg still has 5 of its own true size left even after the
        // aggressor's 15 is satisfied, so it replenishes one last time trailing the final
        // match too; events[^1] can't be assumed to be the match itself here.
        Assert.IsInstanceOf<CreateOrderConfirmed>(events[0]);
        var lastMatch = events.OfType<OrdersMatched>().Last();
        Assert.AreEqual(OrderStatus.Filled, lastMatch.Fills[1].Order.Status);
        Assert.AreEqual(15, lastMatch.Fills[1].Order.FilledQuantity);

        var sellLevels = Book.GetLevels(Side.Sell, 10);
        Assert.AreEqual(1, sellLevels.Count);
        Assert.AreEqual(3, sellLevels[0].Quantity); // 20 - 15 = 5 true remaining, displayed capped to peak 3
    }

    [Test]
    public void UpdateOrder_IcebergQuantityIncrease_DoesNotLosePriority()
    {
        // arrange - iceberg rests first, a plain order arrives behind it
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 10, 100,
            maxVisibleQuantity: 3);
        TimeProvider.SetCurrentTime(Now2);
        Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Buy, 5, 100);
        TimeProvider.SetCurrentTime(Now3);

        // act - grow the iceberg's total size (hidden reserve only, peak is immutable)
        Book.UpdateOrder(CompanyId1, OrderId1B, OrderId1, newTotalQuantity: 15);
        TimeProvider.SetCurrentTime(Now4);

        // a sell should still match the iceberg first - no priority lost
        var events = Book.CreateLimitOrder(CompanyId3, OrderId3, new OrderValidity.Day(), Side.Sell, 3, 100);

        // assert
        var matched = events[1] as OrdersMatched;
        Assert.IsNotNull(matched);
        Assert.AreEqual(OrderId1B, matched.Fills[0].ClientOrderId);
    }

    [Test]
    public void UpdateOrder_PlainOrderQuantityIncrease_StillLosesPriority()
    {
        // regression - confirms the exception added for icebergs doesn't affect plain orders
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 10, 100);
        TimeProvider.SetCurrentTime(Now2);
        Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Buy, 5, 100);
        TimeProvider.SetCurrentTime(Now3);

        // act
        Book.UpdateOrder(CompanyId1, OrderId1B, OrderId1, newTotalQuantity: 15);
        TimeProvider.SetCurrentTime(Now4);

        // a sell should now match Company2 first - Company1 lost priority by growing
        var events = Book.CreateLimitOrder(CompanyId3, OrderId3, new OrderValidity.Day(), Side.Sell, 3, 100);

        // assert
        var matched = events[1] as OrdersMatched;
        Assert.IsNotNull(matched);
        Assert.AreEqual(OrderId2, matched.Fills[0].ClientOrderId);
    }

    [Test]
    public void AggressorIsIceberg_OwnDisplayedPortionCapsEachFill()
    {
        // arrange - a small resting sell that fully consumes the aggressor's first peak, and
        // a larger one behind it for the aggressor to keep working through
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 3, 100);
        TimeProvider.SetCurrentTime(Now2);
        Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Sell, 20, 100);
        TimeProvider.SetCurrentTime(Now3);

        // act - the aggressor itself is an iceberg: total 10, peak 3
        var events = Book.CreateLimitOrder(CompanyId3, OrderId3, new OrderValidity.Day(), Side.Buy, 10, 100,
            maxVisibleQuantity: 3);

        // assert - each fill is capped at the aggressor's own displayed peak (3), not its full
        // remaining size, down to whatever's left at the end (3, 3, 3, then 1). The aggressor
        // itself replenishes (and loses priority) after each of the first three fills, but not
        // the last, since it's fully filled at that point.
        Assert.AreEqual(8, events.Count);
        var matches = events.OfType<OrdersMatched>().ToList();
        Assert.AreEqual(4, matches.Count);
        Assert.AreEqual(3, matches[0].Quantity);
        Assert.AreEqual(3, matches[1].Quantity);
        Assert.AreEqual(3, matches[2].Quantity);
        Assert.AreEqual(1, matches[3].Quantity);
        Assert.AreEqual(3, events.OfType<UpdateOrderConfirmed>().Count());

        var lastFill = matches[3].Fills[1];
        Assert.AreEqual(OrderId3, lastFill.ClientOrderId);
        Assert.AreEqual(OrderStatus.Filled, lastFill.Order.Status);
        Assert.AreEqual(10, lastFill.Order.FilledQuantity);

        Assert.AreEqual(0, Book.GetLevels(Side.Buy, 10).Count);
        var sellLevels = Book.GetLevels(Side.Sell, 10);
        Assert.AreEqual(1, sellLevels.Count);
        Assert.AreEqual(13, sellLevels[0].Quantity); // Company2's 20 - 7 consumed
    }
}
