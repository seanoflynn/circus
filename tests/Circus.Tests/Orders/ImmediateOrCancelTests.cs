using Circus.Actions;
using Circus.Events;
using Circus.Tests.Helpers;
using Circus.Time;
using NUnit.Framework;

namespace Circus.Tests.Orders;

// ImmediateOrCancel unifies what were previously two separate validities: MinQuantity unset
// behaves like classic IOC/FillAndKill (fills what's available, cancels the rest, no minimum
// required); MinQuantity set to the order's own Quantity reproduces FillOrKill exactly (the
// whole order fills or nothing does); MinQuantity anywhere in between requires at least that
// much to fill immediately or nothing fills at all.
[TestFixture]
public class ImmediateOrCancelTests
{
    private static readonly Instrument Gold = new("GCZ6", 10, 10);

    private static readonly DateTime Now1 = new(2000, 1, 1, 12, 0, 0);
    private static readonly DateTime Now2 = new(2000, 1, 1, 12, 1, 0);
    private static readonly DateTime Now3 = new(2000, 1, 1, 12, 2, 0);
    private static readonly DateTime Now4 = new(2000, 1, 1, 12, 3, 0);
    private static readonly DateTime Now5 = new(2000, 1, 1, 12, 4, 0);

    private static readonly string CompanyId1 = "Company1";
    private static readonly string CompanyId2 = "Company2";
    private static readonly string CompanyId3 = "Company3";
    private static readonly string CompanyId4 = "Company4";
    private static readonly string CompanyId5 = "Company5";
    private static readonly string CompanyId6 = "Company6";

    private static readonly string OrderId1 = "Order1";
    private static readonly string OrderId2 = "Order2";
    private static readonly string OrderId3 = "Order3";
    private static readonly string OrderId4 = "Order4";
    private static readonly string OrderId5 = "Order5";
    private static readonly string OrderId6 = "Order6";

    private static ManualClock Clock;
    private static LevelTrackingOrderBook Book;

    [SetUp]
    public void SetUp()
    {
        Clock = new ManualClock(Now1);
        Book = new LevelTrackingOrderBook(Gold, Clock);
    }

    // ----- no MinQuantity (classic IOC/FillAndKill behavior) -----

    [Test]
    public void LimitOrder_FullFill_Success()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 3, 100);
        Clock.SetCurrentTime(Now2);

        // act
        var events = Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.ImmediateOrCancel(), Side.Buy, 3, 100);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(2, events.Count);

        var matched = events[1] as OrdersMatched;
        Assert.IsNotNull(matched);
        Assert.AreEqual(100, matched.Price);
        Assert.AreEqual(3, matched.Quantity);

        Assert.AreEqual(OrderId2, matched.Fills[1].ClientOrderId);
        Assert.AreEqual(OrderStatus.Filled, matched.Fills[1].Order.Status);
        Assert.AreEqual(OrderType.Limit, matched.Fills[1].Order.Type);
        Assert.AreEqual(new OrderValidity.ImmediateOrCancel(), matched.Fills[1].Order.OrderValidity);
        Assert.AreEqual(3, matched.Fills[1].Order.FilledQuantity);
        Assert.AreEqual(0, matched.Fills[1].Order.RemainingQuantity);
    }

    [Test]
    public void LimitOrder_PartialFill_RemainderCancelled()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 2, 100);
        Clock.SetCurrentTime(Now2);

        // act
        var events = Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.ImmediateOrCancel(), Side.Buy, 5, 100);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(3, events.Count);

        var matched = events[1] as OrdersMatched;
        Assert.IsNotNull(matched);
        Assert.AreEqual(100, matched.Price);
        Assert.AreEqual(2, matched.Quantity);

        var cancelled = events[2] as CancelOrderConfirmed;
        Assert.IsNotNull(cancelled);
        Assert.AreEqual(Gold.Symbol, cancelled.Symbol);
        Assert.AreEqual(Now2, cancelled.Time);
        Assert.AreEqual(CompanyId2, cancelled.CompanyId);
        Assert.AreEqual(OrderCancelledReason.ImmediateOrCancelNotFilled, cancelled.Reason);
        Assert.AreEqual(OrderId2, cancelled.Order.ClientOrderId);
        Assert.AreEqual(OrderStatus.Cancelled, cancelled.Order.Status);
        Assert.AreEqual(OrderType.Limit, cancelled.Order.Type);
        Assert.AreEqual(new OrderValidity.ImmediateOrCancel(), cancelled.Order.OrderValidity);
        Assert.AreEqual(5, cancelled.Order.Quantity);
        Assert.AreEqual(2, cancelled.Order.FilledQuantity);
        Assert.AreEqual(0, cancelled.Order.RemainingQuantity);

        // book has nothing resting from either order
        Assert.AreEqual(0, Book.GetLevels(Side.Buy, 10).Count);
        Assert.AreEqual(0, Book.GetLevels(Side.Sell, 10).Count);
    }

    [Test]
    public void LimitOrder_EmptyBook_ImmediatelyCancelled()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);

        // act
        var events = Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.ImmediateOrCancel(), Side.Buy, 5, 100);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(2, events.Count);

        var created = events[0] as CreateOrderConfirmed;
        Assert.IsNotNull(created);

        var cancelled = events[1] as CancelOrderConfirmed;
        Assert.IsNotNull(cancelled);
        Assert.AreEqual(OrderCancelledReason.ImmediateOrCancelNotFilled, cancelled.Reason);
        Assert.AreEqual(OrderStatus.Cancelled, cancelled.Order.Status);
        Assert.AreEqual(0, cancelled.Order.FilledQuantity);
        Assert.AreEqual(0, cancelled.Order.RemainingQuantity);

        Assert.AreEqual(0, Book.GetLevels(Side.Buy, 10).Count);
    }

    [Test]
    public void MarketOrder_PartialFillWithinProtection_RemainderCancelled()
    {
        // arrange
        var wideProtectionGold = new Instrument("GCZ6", 10, 20);
        var book = new LevelTrackingOrderBook(wideProtectionGold, Clock);
        book.UpdateStatus(OrderBookStatus.Open);
        book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 2, 500);
        Clock.SetCurrentTime(Now2);

        // act
        // NB: a Market + GTC/Day order in the same situation would instead rest the
        // remainder as a limit order at the protected price (see MarketOrder_Success).
        var events = book.CreateMarketOrder(CompanyId2, OrderId2, new OrderValidity.ImmediateOrCancel(), Side.Buy, 5);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(3, events.Count);

        var matched = events[1] as OrdersMatched;
        Assert.IsNotNull(matched);
        Assert.AreEqual(500, matched.Price);
        Assert.AreEqual(2, matched.Quantity);

        var cancelled = events[2] as CancelOrderConfirmed;
        Assert.IsNotNull(cancelled);
        Assert.AreEqual(OrderCancelledReason.ImmediateOrCancelNotFilled, cancelled.Reason);
        Assert.AreEqual(OrderId2, cancelled.Order.ClientOrderId);
        Assert.AreEqual(OrderType.Market, cancelled.Order.Type);
        Assert.AreEqual(700, cancelled.Order.Price);
        Assert.AreEqual(OrderStatus.Cancelled, cancelled.Order.Status);
        Assert.AreEqual(2, cancelled.Order.FilledQuantity);
        Assert.AreEqual(0, cancelled.Order.RemainingQuantity);

        Assert.AreEqual(0, book.GetLevels(Side.Buy, 10).Count);
    }

    [Test]
    public void StopLimitOrder_TriggersAndPartiallyFills_RemainderCancelled()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 5, 500);
        Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Buy, 5, 500); // last traded price = 500
        Clock.SetCurrentTime(Now2);

        // IOC stop-limit buy: triggers when price rises to/above 520, then willing to pay up to 530
        Book.CreateStopLimitOrder(CompanyId3, OrderId3, new OrderValidity.ImmediateOrCancel(), Side.Buy, 5, 530, 520);
        Clock.SetCurrentTime(Now3);

        // only 2 available to fill the stop once triggered
        Book.CreateLimitOrder(CompanyId4, OrderId4, new OrderValidity.Day(), Side.Sell, 2, 530);
        Clock.SetCurrentTime(Now4);
        Book.CreateLimitOrder(CompanyId5, OrderId5, new OrderValidity.Day(), Side.Buy, 1, 520);
        Clock.SetCurrentTime(Now5);

        // act - trade at 520 triggers the stop
        var events = Book.CreateLimitOrder(CompanyId6, OrderId6, new OrderValidity.Day(), Side.Sell, 1, 520);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(5, events.Count);

        Assert.IsInstanceOf<CreateOrderConfirmed>(events[0]);
        Assert.IsInstanceOf<OrdersMatched>(events[1]);

        var triggered = events[2] as UpdateOrderConfirmed;
        Assert.IsNotNull(triggered);
        Assert.AreEqual(OrderId3, triggered.Order.ClientOrderId);
        Assert.AreEqual(OrderType.Limit, triggered.Order.Type);
        Assert.AreEqual(530, triggered.Order.Price);

        var stopMatch = events[3] as OrdersMatched;
        Assert.IsNotNull(stopMatch);
        Assert.AreEqual(530, stopMatch.Price);
        Assert.AreEqual(2, stopMatch.Quantity);

        var cancelled = events[4] as CancelOrderConfirmed;
        Assert.IsNotNull(cancelled);
        Assert.AreEqual(OrderId3, cancelled.Order.ClientOrderId);
        Assert.AreEqual(OrderCancelledReason.ImmediateOrCancelNotFilled, cancelled.Reason);
        Assert.AreEqual(OrderStatus.Cancelled, cancelled.Order.Status);
        Assert.AreEqual(OrderType.Limit, cancelled.Order.Type);
        Assert.AreEqual(new OrderValidity.ImmediateOrCancel(), cancelled.Order.OrderValidity);
        Assert.AreEqual(2, cancelled.Order.FilledQuantity);
        Assert.AreEqual(0, cancelled.Order.RemainingQuantity);

        Assert.AreEqual(0, Book.GetLevels(Side.Buy, 10).Count);
        Assert.AreEqual(0, Book.GetLevels(Side.Sell, 10).Count);
    }

    // ----- MinQuantity == full order quantity (FillOrKill-equivalent behavior) -----

    [Test]
    public void MinQuantity_EqualsFullQuantity_SufficientLiquidityAtSingleLevel_FullyFilled()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 5, 100);
        Clock.SetCurrentTime(Now2);

        // act
        var events = Book.CreateLimitOrder(CompanyId2, OrderId2,
            new OrderValidity.ImmediateOrCancel { MinQuantity = 5 }, Side.Buy, 5, 100);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(2, events.Count);

        var matched = events[1] as OrdersMatched;
        Assert.IsNotNull(matched);
        Assert.AreEqual(100, matched.Price);
        Assert.AreEqual(5, matched.Quantity);
        Assert.AreEqual(OrderStatus.Filled, matched.Fills[1].Order.Status);
        Assert.AreEqual(5, matched.Fills[1].Order.FilledQuantity);
        Assert.AreEqual(0, matched.Fills[1].Order.RemainingQuantity);
    }

    [Test]
    public void MinQuantity_EqualsFullQuantity_SufficientLiquidityAcrossMultipleLevels_FullyFilled()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 3, 100);
        Clock.SetCurrentTime(Now2);
        Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Sell, 3, 110);
        Clock.SetCurrentTime(Now3);

        // act - only fills if the 3@100 and 2@110 levels are summed together
        var events = Book.CreateLimitOrder(CompanyId3, OrderId3,
            new OrderValidity.ImmediateOrCancel { MinQuantity = 5 }, Side.Buy, 5, 110);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(3, events.Count);

        var matched1 = events[1] as OrdersMatched;
        Assert.IsNotNull(matched1);
        Assert.AreEqual(100, matched1.Price);
        Assert.AreEqual(3, matched1.Quantity);

        var matched2 = events[2] as OrdersMatched;
        Assert.IsNotNull(matched2);
        Assert.AreEqual(110, matched2.Price);
        Assert.AreEqual(2, matched2.Quantity);
        Assert.AreEqual(OrderStatus.Filled, matched2.Fills[1].Order.Status);
        Assert.AreEqual(OrderId3, matched2.Fills[1].ClientOrderId);
        Assert.AreEqual(5, matched2.Fills[1].Order.FilledQuantity);
        Assert.AreEqual(0, matched2.Fills[1].Order.RemainingQuantity);
    }

    [Test]
    public void MinQuantity_EqualsFullQuantity_InsufficientLiquidity_Rejected()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 2, 100);
        Clock.SetCurrentTime(Now2);

        // act
        var events = Book.CreateLimitOrder(CompanyId2, OrderId2,
            new OrderValidity.ImmediateOrCancel { MinQuantity = 5 }, Side.Buy, 5, 100);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(1, events.Count);

        var rejected = events[0] as CreateOrderRejected;
        Assert.IsNotNull(rejected);
        Assert.AreEqual(Gold.Symbol, rejected.Symbol);
        Assert.AreEqual(Now2, rejected.Time);
        Assert.AreEqual(CompanyId2, rejected.CompanyId);
        Assert.AreEqual(OrderId2, rejected.ClientOrderId);
        Assert.AreEqual(OrderRejectedReason.InsufficientLiquidityForMinQuantity, rejected.Reason);

        // book is completely untouched - no partial fill leaked through
        var levels = Book.GetLevels(Side.Sell, 10);
        Assert.AreEqual(1, levels.Count);
        Assert.AreEqual(100, levels[0].Price);
        Assert.AreEqual(2, levels[0].Quantity);
        Assert.AreEqual(0, Book.GetLevels(Side.Buy, 10).Count);
    }

    [Test]
    public void MinQuantity_EqualsFullQuantity_StopLimitOrder_TriggersWithInsufficientLiquidity_Cancelled()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 5, 500);
        Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Buy, 5, 500); // last traded price = 500
        Clock.SetCurrentTime(Now2);

        // FOK-equivalent stop-limit buy: triggers when price rises to/above 520, then willing to pay up to 530
        Book.CreateStopLimitOrder(CompanyId3, OrderId3, new OrderValidity.ImmediateOrCancel { MinQuantity = 5 },
            Side.Buy, 5, 530, 520);
        Clock.SetCurrentTime(Now3);

        // only 2 available - not enough to fill the stop's 5 in full
        Book.CreateLimitOrder(CompanyId4, OrderId4, new OrderValidity.Day(), Side.Sell, 2, 530);
        Clock.SetCurrentTime(Now4);
        Book.CreateLimitOrder(CompanyId5, OrderId5, new OrderValidity.Day(), Side.Buy, 1, 520);
        Clock.SetCurrentTime(Now5);

        // act - trade at 520 triggers the stop
        var events = Book.CreateLimitOrder(CompanyId6, OrderId6, new OrderValidity.Day(), Side.Sell, 1, 520);

        // assert
        Assert.IsNotNull(events);
        Assert.AreEqual(3, events.Count);

        Assert.IsInstanceOf<CreateOrderConfirmed>(events[0]);
        Assert.IsInstanceOf<OrdersMatched>(events[1]);

        var cancelled = events[2] as CancelOrderConfirmed;
        Assert.IsNotNull(cancelled);
        Assert.AreEqual(OrderId3, cancelled.Order.ClientOrderId);
        Assert.AreEqual(OrderCancelledReason.ImmediateOrCancelNotFilled, cancelled.Reason);
        Assert.AreEqual(OrderStatus.Cancelled, cancelled.Order.Status);
        Assert.AreEqual(OrderType.StopLimit, cancelled.Order.Type);
        Assert.AreEqual(new OrderValidity.ImmediateOrCancel { MinQuantity = 5 }, cancelled.Order.OrderValidity);
        Assert.AreEqual(0, cancelled.Order.FilledQuantity);
        Assert.AreEqual(0, cancelled.Order.RemainingQuantity);

        // the 2@530 resting sell was never touched
        var levels = Book.GetLevels(Side.Sell, 10);
        Assert.AreEqual(1, levels.Count);
        Assert.AreEqual(530, levels[0].Price);
        Assert.AreEqual(2, levels[0].Quantity);
    }

    // ----- MinQuantity between 1 and the full order quantity -----

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(6)]
    public void MinQuantity_OutsideValidRange_Rejected(int minQuantity)
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);

        // act
        var events = Book.CreateLimitOrder(CompanyId1, OrderId1,
            new OrderValidity.ImmediateOrCancel { MinQuantity = minQuantity }, Side.Buy, 5, 100);

        // assert
        var rejected = events[0] as CreateOrderRejected;
        Assert.IsNotNull(rejected);
        Assert.AreEqual(OrderRejectedReason.MinQuantityOutOfRange, rejected.Reason);
    }

    [Test]
    public void MinQuantity_SufficientButNotFullSize_FillsAvailable_CancelsRemainder()
    {
        // arrange - only 3 available, less than the order's full size of 5, but enough to
        // satisfy a MinQuantity of 3
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 3, 100);
        Clock.SetCurrentTime(Now2);

        // act
        var events = Book.CreateLimitOrder(CompanyId2, OrderId2,
            new OrderValidity.ImmediateOrCancel { MinQuantity = 3 }, Side.Buy, 5, 100);

        // assert - proceeds like classic IOC once the gate is satisfied: fills what's
        // available, cancels the rest
        Assert.AreEqual(3, events.Count);
        Assert.IsInstanceOf<CreateOrderConfirmed>(events[0]);

        var matched = events[1] as OrdersMatched;
        Assert.IsNotNull(matched);
        Assert.AreEqual(3, matched.Quantity);

        var cancelled = events[2] as CancelOrderConfirmed;
        Assert.IsNotNull(cancelled);
        Assert.AreEqual(OrderCancelledReason.ImmediateOrCancelNotFilled, cancelled.Reason);
        Assert.AreEqual(3, cancelled.Order.FilledQuantity);
        Assert.AreEqual(0, cancelled.Order.RemainingQuantity);
    }

    [Test]
    public void MinQuantity_Insufficient_RejectedOutright_NothingFills()
    {
        // arrange - only 2 available, below the MinQuantity of 3
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 2, 100);
        Clock.SetCurrentTime(Now2);

        // act
        var events = Book.CreateLimitOrder(CompanyId2, OrderId2,
            new OrderValidity.ImmediateOrCancel { MinQuantity = 3 }, Side.Buy, 5, 100);

        // assert - rejected outright, not even a partial fill below the minimum
        Assert.AreEqual(1, events.Count);
        var rejected = events[0] as CreateOrderRejected;
        Assert.IsNotNull(rejected);
        Assert.AreEqual(OrderRejectedReason.InsufficientLiquidityForMinQuantity, rejected.Reason);

        // the resting sell is completely untouched
        var sellLevels = Book.GetLevels(Side.Sell, 10);
        Assert.AreEqual(1, sellLevels.Count);
        Assert.AreEqual(2, sellLevels[0].Quantity);
        Assert.AreEqual(0, Book.GetLevels(Side.Buy, 10).Count);
    }

    [Test]
    public void MinQuantity_TriggeredStop_GoesThroughSameGate()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 5, 500);
        Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Buy, 5, 500); // last traded price = 500
        Clock.SetCurrentTime(Now2);

        // IOC stop-limit buy: triggers at/above 520, willing to pay up to 530, needs at least 2
        Book.CreateStopLimitOrder(CompanyId3, OrderId3, new OrderValidity.ImmediateOrCancel { MinQuantity = 2 },
            Side.Buy, 5, 530, 520);
        Clock.SetCurrentTime(Now3);

        // only 1 available once the stop triggers - below its MinQuantity of 2
        Book.CreateLimitOrder(CompanyId4, OrderId4, new OrderValidity.Day(), Side.Sell, 1, 530);
        Clock.SetCurrentTime(Now4);
        Book.CreateLimitOrder(CompanyId5, OrderId5, new OrderValidity.Day(), Side.Buy, 1, 520);
        Clock.SetCurrentTime(Now5);

        // act - a sell crossing the just-rested 520 buy prints a trade at 520, triggering the stop
        var events = Book.CreateLimitOrder(CompanyId6, OrderId6, new OrderValidity.Day(), Side.Sell, 1, 520);

        // assert - stop triggers, but the available 1 unit doesn't meet its MinQuantity of 2,
        // so it's cancelled directly rather than being converted to a working limit order
        Assert.AreEqual(3, events.Count);
        Assert.IsInstanceOf<CreateOrderConfirmed>(events[0]);
        Assert.IsInstanceOf<OrdersMatched>(events[1]);

        var cancelled = events[2] as CancelOrderConfirmed;
        Assert.IsNotNull(cancelled);
        Assert.AreEqual(OrderId3, cancelled.Order.ClientOrderId);
        Assert.AreEqual(OrderCancelledReason.ImmediateOrCancelNotFilled, cancelled.Reason);
        Assert.AreEqual(0, cancelled.Order.FilledQuantity);
        Assert.AreEqual(0, cancelled.Order.RemainingQuantity);

        // the 1 unit at 530 is untouched
        var sellLevels = Book.GetLevels(Side.Sell, 10);
        Assert.AreEqual(1, sellLevels.Count);
        Assert.AreEqual(530, sellLevels[0].Price);
        Assert.AreEqual(1, sellLevels[0].Quantity);
    }
}
