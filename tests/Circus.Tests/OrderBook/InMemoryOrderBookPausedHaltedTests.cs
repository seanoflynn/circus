using Circus.OrderBook;
using Circus.OrderBook.Actions;
using Circus.OrderBook.Events;
using Circus.Time;
using NUnit.Framework;

namespace Circus.Tests.OrderBook;

// Paused and Halted are both interruptions within a session, so neither may behave like the
// start of one (PreOpen) or the end of one (Closed). What separates the two from each other is
// price discovery: a pause keeps quoting and resolves into a print, a halt publishes nothing.
[TestFixture]
public class InMemoryOrderBookPausedHaltedTests
{
    private static readonly Security Sec = new("GCZ6", SecurityType.Future, 10, 10);

    private static readonly DateTime Now1 = new(2000, 1, 1, 12, 0, 0);
    private static readonly DateTime Now2 = new(2000, 1, 1, 12, 1, 0);
    private static readonly DateTime NextDay = new(2000, 1, 2, 12, 0, 0);

    private static readonly string CompanyId1 = "Company1";
    private static readonly string CompanyId2 = "Company2";

    private static readonly string OrderId1 = "Order1";
    private static readonly string OrderId2 = "Order2";
    private static readonly string CancelId1 = "Cancel1";

    private ManualClock Clock;
    private IOrderBook Book;

    [SetUp]
    public void SetUp()
    {
        Clock = new ManualClock(Now1);
        Book = new InMemoryOrderBook(Sec, Clock);
    }

    [TestCase(OrderBookStatus.Paused)]
    [TestCase(OrderBookStatus.Halted)]
    public void UpdateStatus_ReachesTheNewStatuses(OrderBookStatus status)
    {
        // act
        var events = Book.UpdateStatus(status);

        // assert
        Assert.AreEqual(1, events.Count);
        var statusChanged = events[0] as StatusChanged;
        Assert.IsNotNull(statusChanged);
        Assert.AreEqual(status, statusChanged.Status);
        Assert.AreEqual(Now1, statusChanged.Time);
        Assert.AreEqual(status, Book.Status);
    }

    [TestCase(OrderBookStatus.Paused)]
    [TestCase(OrderBookStatus.Halted)]
    public void OrderActionsStillAccepted(OrderBookStatus status)
    {
        // arrange - resting from before the interruption
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 5, 100);
        Book.UpdateStatus(status);

        // act - an interruption is not a close, so positions stay manageable
        var created = Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Buy, 5, 90);
        var cancelled = Book.CancelOrder(CompanyId1, CancelId1, OrderId1);

        // assert
        Assert.IsInstanceOf<CreateOrderConfirmed>(created[0]);
        Assert.IsInstanceOf<CancelOrderConfirmed>(cancelled[0]);
    }

    [TestCase(OrderBookStatus.Paused)]
    [TestCase(OrderBookStatus.Halted)]
    public void MarketOrdersRejected(OrderBookStatus status)
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 5, 100);
        Book.UpdateStatus(status);

        // act - nothing is trading, so there is no book to price a market order against
        var events = Book.CreateMarketOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Buy, 5);

        // assert
        var rejected = events[0] as CreateOrderRejected;
        Assert.IsNotNull(rejected);
    }

    [TestCase(OrderBookStatus.Paused)]
    [TestCase(OrderBookStatus.Halted)]
    public void DayOrdersSurvive(OrderBookStatus status)
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 5, 100);
        Clock.SetCurrentTime(Now2);

        // act - only a close that ends the trading day retires day orders
        var events = Book.UpdateStatus(status);

        // assert
        Assert.AreEqual(0, events.OfType<ExpireOrderConfirmed>().Count());

        // and the order is genuinely still resting, not merely unexpired
        Book.UpdateStatus(OrderBookStatus.Open);
        var crossing = Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Sell, 5, 100);
        Assert.AreEqual(1, crossing.OfType<OrdersMatched>().Count());
    }

    [TestCase(OrderBookStatus.Paused)]
    [TestCase(OrderBookStatus.Halted)]
    public void DoesNotStartASession(OrderBookStatus status)
    {
        // arrange - sequence numbers are seeded from the date, and a session start on a later
        // date jumps them to that date's run. An interruption is not a session start, so the
        // counter must simply carry on. PreOpen on the same setup is the contrast, below.
        Book.UpdateStatus(OrderBookStatus.PreOpen);
        Book.UpdateStatus(OrderBookStatus.Open);
        var beforeId = ExchangeOrderIdOf(Book, CompanyId1, OrderId1, Side.Buy, 5, 100);

        // act
        Clock.SetCurrentTime(NextDay);
        Book.UpdateStatus(status);
        var afterId = ExchangeOrderIdOf(Book, CompanyId2, OrderId2, Side.Buy, 5, 90);

        // assert
        Assert.AreEqual(beforeId + 1, afterId, "the interruption did not reseed the run of ids");
    }

    [Test]
    public void PreOpenOnALaterDate_DoesStartASession()
    {
        // arrange - the contrast for DoesNotStartASession: the same steps through PreOpen, which
        // genuinely does start one, so the ids jump to the new date's run
        Book.UpdateStatus(OrderBookStatus.PreOpen);
        Book.UpdateStatus(OrderBookStatus.Open);
        var beforeId = ExchangeOrderIdOf(Book, CompanyId1, OrderId1, Side.Buy, 5, 100);

        // act
        Clock.SetCurrentTime(NextDay);
        Book.UpdateStatus(OrderBookStatus.PreOpen);
        var afterId = ExchangeOrderIdOf(Book, CompanyId2, OrderId2, Side.Buy, 5, 90);

        // assert
        Assert.IsTrue(afterId > beforeId + 1, "a session start reseeds from the new date");
    }

    [Test]
    public void Paused_QuotesTheCrossedBook_AndPrintsOnResuming()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.UpdateStatus(OrderBookStatus.Paused);

        // act - orders accumulate into a cross while paused
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 10, 100);
        var crossing = Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Sell, 10, 100);

        // assert - quoted, not printed
        var quote = crossing.OfType<IndicativePriceChanged>().Last();
        Assert.AreEqual(100, quote.Price);
        Assert.AreEqual(10, quote.Quantity);
        Assert.AreEqual(0, crossing.OfType<OrdersMatched>().Count(), "a pause quotes, it does not trade");

        // act - resuming resolves the accumulated book in one print
        var resumed = Book.UpdateStatus(OrderBookStatus.Open);

        // assert
        var matched = resumed.OfType<OrdersMatched>().Single();
        Assert.AreEqual(100, matched.Price);
        Assert.AreEqual(10, matched.Quantity);
    }

    [Test]
    public void Halted_PublishesNoQuote_AndDoesNotPrintOnResuming()
    {
        // arrange
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.UpdateStatus(OrderBookStatus.Halted);

        // act - the same crossed book a pause would have been quoting
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 10, 100);
        var crossing = Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Sell, 10, 100);

        // assert - withholding price discovery is what makes this a halt
        Assert.AreEqual(0, crossing.OfType<IndicativePriceChanged>().Count());
        Assert.AreEqual(0, crossing.OfType<OrdersMatched>().Count());

        // act - no algorithm means nothing prints on the way out; the cross is resolved by
        // continuous trading once open, not by an uncrossing auction
        var resumed = Book.UpdateStatus(OrderBookStatus.Open);

        // assert
        var matched = resumed.OfType<OrdersMatched>().Single();
        Assert.AreEqual(100, matched.Price);
        Assert.AreEqual(10, matched.Quantity);
    }

    [Test]
    public void VolatilityBandBreach_PausesRatherThanReturningToPreOpen()
    {
        // arrange - a reference of 100 and a 5-tick volatility band
        var security = new Security("GCZ6", SecurityType.Future, 10, 10,
            PriceRestrictions: new PriceRestrictionConfig[] {new VolatilityBand(5)});
        var book = new InMemoryOrderBook(security, Clock);
        book.UpdateStatus(OrderBookStatus.Open, 100);
        book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 5, 200);

        // act - the trade at 200 breaches the band
        var events = book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Buy, 5, 200);

        // assert
        Assert.AreEqual(OrderBookStatus.Paused, book.Status);
        Assert.AreEqual(OrderBookStatus.Paused, events.OfType<StatusChanged>().Single().Status);
    }

    [Test]
    public void Halted_ThenClosed_ExpiresDayOrdersAsNormal()
    {
        // arrange - a halt defers the day's end, it does not cancel it
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 5, 100);
        Book.UpdateStatus(OrderBookStatus.Halted);
        Clock.SetCurrentTime(Now2);

        // act
        var events = Book.UpdateStatus(OrderBookStatus.Closed);

        // assert
        var expired = events.OfType<ExpireOrderConfirmed>().Single();
        Assert.AreEqual(OrderId1, expired.Order.ClientOrderId);
        Assert.AreEqual(OrderStatus.Expired, expired.Order.Status);
    }

    private static long ExchangeOrderIdOf(IOrderBook book, string companyId, string clientOrderId, Side side,
        int quantity, decimal price)
    {
        var events = book.CreateLimitOrder(companyId, clientOrderId, new OrderValidity.GoodTilCanceled(), side,
            quantity, price);
        return long.Parse(events.OfType<CreateOrderConfirmed>().Single().Order.ExchangeOrderId);
    }
}
