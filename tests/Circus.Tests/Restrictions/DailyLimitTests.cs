using Circus.Actions;
using Circus.Events;
using Circus.Time;
using NUnit.Framework;

namespace Circus.Tests.Restrictions;

// A daily limit is the one restriction that neither rejects nor interrupts. The market stays
// open, keeps quoting, trades at the limit and can trade back inside - so almost every
// assertion here is that the book is still Open while refusing to go further.
[TestFixture]
public class DailyLimitTests
{
    private static readonly DateTime Now1 = new(2000, 1, 1, 12, 0, 0);

    private static readonly string CompanyId1 = "Company1";
    private static readonly string CompanyId2 = "Company2";

    private ManualClock Clock;

    [SetUp]
    public void SetUp()
    {
        Clock = new ManualClock(Now1);
    }

    // 5 ticks on a tick size of 10, referenced at 100, so the limits are 50 and 150.
    private IOrderBook LimitedBook()
    {
        var security = new Instrument("GCZ6", 10, 10,
            PriceRestrictions: new PriceRestrictionConfig[]
            {
                new DailyPriceLimit(new PriceLimitWidth.Ticks(5))
            });
        var book = new TimestampingOrderBook(security, Clock);
        book.UpdateStatus(OrderBookStatus.Open, 100);
        return book;
    }

    [Test]
    public void OrderBeyondTheLimit_RejectedOnEntry()
    {
        // arrange
        var book = LimitedBook();

        // act
        var events = book.CreateLimitOrder(CompanyId1, "Order1", new OrderValidity.Day(), Side.Buy, 5, 160);

        // assert - its own reason, not the band's: a band moves with the market and would very
        // likely take this price shortly, a daily limit stands for the session
        var rejected = events[0] as CreateOrderRejected;
        Assert.IsNotNull(rejected);
        Assert.AreEqual(OrderRejectedReason.BeyondDailyPriceLimit, rejected.Reason);
    }

    [Test]
    public void TradingAtTheLimitIsAllowed()
    {
        // arrange
        var book = LimitedBook();

        // act - exactly on the limit
        book.CreateLimitOrder(CompanyId1, "Order1", new OrderValidity.Day(), Side.Sell, 5, 150);
        var events = book.CreateLimitOrder(CompanyId2, "Order2", new OrderValidity.Day(), Side.Buy, 5, 150);

        // assert
        var matched = events.OfType<OrdersMatched>().Single();
        Assert.AreEqual(150, matched.Price);
        Assert.AreEqual(OrderBookStatus.Open, book.Status);
        Assert.AreEqual(0, events.OfType<LimitStateChanged>().Count(), "trading at the limit is not being stuck");
    }

    // Because entry and trades face the same limit, an order beyond it cannot simply be sent -
    // which is the point. Getting one to rest out there means resting it while the limit was
    // wider and then moving the reference so the limit narrows under it. That leaves a book
    // whose own resting order is beyond the ceiling, which is exactly the state a sweep has to
    // refuse: it traded at 200, holds a buy at 240, and is now referenced at 100.
    private IOrderBook BookWithRestingOrderAboveTheLimit()
    {
        var security = new Instrument("GCZ6", 10, 10,
            PriceRestrictions: new PriceRestrictionConfig[]
            {
                new DailyPriceLimit(new PriceLimitWidth.Ticks(5))
            });
        var book = new TimestampingOrderBook(security, Clock);

        book.UpdateStatus(OrderBookStatus.Open, 200);
        book.CreateLimitOrder(CompanyId1, "Sell1", new OrderValidity.Day(), Side.Sell, 5, 200);
        book.CreateLimitOrder(CompanyId2, "Buy1", new OrderValidity.Day(), Side.Buy, 5, 200);
        book.CreateLimitOrder(CompanyId2, "Buy2", new OrderValidity.Day(), Side.Buy, 5, 240);

        // The clock has to move on before whatever the test does next. Matcher.Run picks the
        // resting side with a strict ModifiedTime comparison, so orders sharing a timestamp
        // hand it to the sell - and price-time prints at the resting order's price. Left at one
        // instant, an incoming sell would price the trade at its own limit rather than at the
        // buy resting above the ceiling, and there would be nothing out of range to refuse.
        Clock.SetCurrentTime(Now1.AddMinutes(1));
        book.UpdateStatus(OrderBookStatus.Open, 100);
        return book;
    }

    [Test]
    public void TradeThroughTheLimit_BlockedWithoutHaltingOrPausing()
    {
        // arrange
        var book = BookWithRestingOrderAboveTheLimit();

        // act - a sell at 150 is inside the limit and perfectly enterable, but it crosses the
        // resting buy at 240, and that is where the trade would print
        var events = book.CreateLimitOrder(CompanyId1, "Sell2", new OrderValidity.Day(), Side.Sell, 5, 150);

        // assert - nothing printed, and the market is neither paused nor halted
        Assert.IsInstanceOf<CreateOrderConfirmed>(events[0]);
        Assert.AreEqual(0, events.OfType<OrdersMatched>().Count());
        Assert.AreEqual(OrderBookStatus.Open, book.Status, "a limit is not a halt");

        // ...and it says which way it is stuck: the blocked price is above the last trade, so
        // buyers are the ones who cannot get through
        var limited = events.OfType<LimitStateChanged>().Single();
        Assert.AreEqual(Side.Buy, limited.Side);
        Assert.AreEqual(240, limited.Price);
    }

    [Test]
    public void LimitState_PublishedOnceAndReleasedByATrade()
    {
        // arrange - already limit locked
        var book = BookWithRestingOrderAboveTheLimit();
        book.CreateLimitOrder(CompanyId1, "Sell2", new OrderValidity.Day(), Side.Sell, 5, 150);

        // act - another sweep against the same wall says nothing new
        var again = book.CreateLimitOrder(CompanyId1, "Sell3", new OrderValidity.Day(), Side.Sell, 5, 140);
        Assert.AreEqual(0, again.OfType<LimitStateChanged>().Count(), "already said, and still true");

        // act - the order out beyond the ceiling is pulled, and the book trades inside the limit
        book.CancelOrder(CompanyId2, "Cancel1", "Buy2");
        var traded = book.CreateLimitOrder(CompanyId2, "Buy3", new OrderValidity.Day(), Side.Buy, 5, 140);

        // assert - trading again, and said so
        Assert.AreEqual(1, traded.OfType<OrdersMatched>().Count());
        var released = traded.OfType<LimitStateChanged>().Single();
        Assert.IsNull(released.Side);
        Assert.IsNull(released.Price);
    }

    [Test]
    public void PercentageWidth_ResolvesAgainstTheReference()
    {
        // arrange - 7 percent of a reference of 1000, so 70 either side
        var security = new Instrument("GCZ6", 10, 10,
            PriceRestrictions: new PriceRestrictionConfig[]
            {
                new DailyPriceLimit(new PriceLimitWidth.Percent(7))
            });
        var book = new TimestampingOrderBook(security, Clock);
        book.UpdateStatus(OrderBookStatus.Open, 1000);

        // act + assert - 1070 is exactly on the ceiling
        var atEdge = book.CreateLimitOrder(CompanyId1, "Order1", new OrderValidity.Day(), Side.Buy, 5, 1070);
        Assert.IsInstanceOf<CreateOrderConfirmed>(atEdge[0]);

        var beyond = book.CreateLimitOrder(CompanyId1, "Order2", new OrderValidity.Day(), Side.Buy, 5, 1080);
        Assert.AreEqual(OrderRejectedReason.BeyondDailyPriceLimit, (beyond[0] as CreateOrderRejected)?.Reason);
    }

    [Test]
    public void NoReferenceYet_LimitInactive()
    {
        // arrange - a percentage of nothing is not a width
        var security = new Instrument("GCZ6", 10, 10,
            PriceRestrictions: new PriceRestrictionConfig[]
            {
                new DailyPriceLimit(new PriceLimitWidth.Percent(7))
            });
        var book = new TimestampingOrderBook(security, Clock);
        book.UpdateStatus(OrderBookStatus.Open);

        // act
        var events = book.CreateLimitOrder(CompanyId1, "Order1", new OrderValidity.Day(), Side.Buy, 5, 1_000_000);

        // assert
        Assert.IsInstanceOf<CreateOrderConfirmed>(events[0]);
    }
}
