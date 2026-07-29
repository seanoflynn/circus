using Circus.OrderBook;
using Circus.OrderBook.Actions;
using Circus.OrderBook.Events;
using Circus.Time;
using NUnit.Framework;

namespace Circus.Tests.OrderBook.Restrictions;

// The two things a volatility interruption does beyond pausing: it can refuse to end at a price
// still too far out, and a range anchored on the session catches drift that one following the
// market never sees.
[TestFixture]
public class VolatilityInterruptionTests
{
    private static readonly DateTime Now1 = new(2000, 1, 1, 12, 0, 0);
    private static readonly TimeSpan PauseFor = TimeSpan.FromMinutes(2);

    private static readonly string CompanyId1 = "Company1";
    private static readonly string CompanyId2 = "Company2";

    private static readonly string OrderId1 = "Order1";
    private static readonly string OrderId2 = "Order2";

    private ManualClock Clock;

    [SetUp]
    public void SetUp()
    {
        Clock = new ManualClock(Now1);
    }

    // Ordinary range 5 ticks, extended range 8, pausing for two minutes. On a tick size of 10
    // that is 50 and 80 either side of the reference.
    private static Security ExtendingSecurity() =>
        new("GCZ6", SecurityType.Future, 10, 10,
            PriceRestrictions: new PriceRestrictionConfig[]
            {
                new VolatilityBand(5, PauseFor: PauseFor, ExtendedRangeTicks: 8)
            });

    [Test]
    public void InterruptionExtends_WhenItWouldEndBeyondTheExtendedRange()
    {
        // arrange - referenced at 100, then a trade at 200 breaches the ordinary range and
        // pauses the book, leaving the two orders crossed and unfilled
        var book = new InMemoryOrderBook(ExtendingSecurity(), Clock);
        book.UpdateStatus(OrderBookStatus.Open, 100);
        book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 5, 200);
        book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Buy, 5, 200);
        Assert.AreEqual(OrderBookStatus.Paused, book.Status);

        // act - the pause runs out, but the auction would still print at 200, which is 100 from
        // the reference and so outside the extended range of 80
        Clock.SetCurrentTime(Now1 + PauseFor);
        var events = book.AdvanceTime();

        // assert - the interruption keeps running rather than resolving out there
        Assert.AreEqual(OrderBookStatus.Paused, book.Status);
        Assert.AreEqual(0, events.OfType<OrdersMatched>().Count(), "nothing printed");

        var stillPaused = events.OfType<StatusChanged>().Single();
        Assert.AreEqual(OrderBookStatus.Paused, stillPaused.Status);
        Assert.AreEqual(StatusChangeReason.PriceRestriction, stillPaused.Reason);
    }

    [Test]
    public void InterruptionEnds_OnceItWouldPrintInsideTheExtendedRange()
    {
        // arrange - as above, paused with a would-be print at 200
        var book = new InMemoryOrderBook(ExtendingSecurity(), Clock);
        book.UpdateStatus(OrderBookStatus.Open, 100);
        book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 5, 200);
        book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Buy, 5, 200);

        Clock.SetCurrentTime(Now1 + PauseFor);
        book.AdvanceTime();
        Assert.AreEqual(OrderBookStatus.Paused, book.Status, "extended once");

        // act - the orders that would have printed out there are pulled, and a cross at 180
        // takes their place. 180 is 80 from the reference: outside the ordinary range that
        // caused the interruption, but exactly on the edge of the extended one.
        book.CancelOrder(CompanyId1, "Cancel1", OrderId1);
        book.CancelOrder(CompanyId2, "Cancel2", OrderId2);
        book.CreateLimitOrder(CompanyId1, "Order3", new OrderValidity.Day(), Side.Sell, 5, 180);
        book.CreateLimitOrder(CompanyId2, "Order4", new OrderValidity.Day(), Side.Buy, 5, 180);

        Clock.SetCurrentTime(Now1 + PauseFor + PauseFor);
        var events = book.AdvanceTime();

        // assert - the interruption resolves, printing at a price the ordinary range would
        // never have allowed to trade continuously
        Assert.AreEqual(OrderBookStatus.Open, book.Status);
        var matched = events.OfType<OrdersMatched>().Single();
        Assert.AreEqual(180, matched.Price);
        Assert.AreEqual(5, matched.Quantity);
    }

    [Test]
    public void StaticRange_CatchesDriftThatTheDynamicRangeDoesNot()
    {
        // arrange - a dynamic range of 3 ticks alongside a static range of 5, referenced at 100.
        // Each step below is well inside the dynamic range; it is the distance from where the
        // day started that eventually trips.
        var security = new Security("GCZ6", SecurityType.Future, 10, 10,
            PriceRestrictions: new PriceRestrictionConfig[]
            {
                new VolatilityBand(3),
                new StaticPriceRange(5)
            });
        var book = new InMemoryOrderBook(security, Clock);
        book.UpdateStatus(OrderBookStatus.Open, 100);

        // act + assert - 120 is 2 ticks from the reference, inside both
        Trade(book, 1, 120);
        Assert.AreEqual(OrderBookStatus.Open, book.Status);

        // 140 is 2 ticks from the last trade and 4 from the reference, still inside both
        Trade(book, 2, 140);
        Assert.AreEqual(OrderBookStatus.Open, book.Status);

        // 160 is 2 ticks from the last trade - the dynamic range is content - but 6 from the
        // reference, which the static range is not
        Trade(book, 3, 160);
        Assert.AreEqual(OrderBookStatus.Paused, book.Status);
    }

    private void Trade(IOrderBook book, int n, decimal price)
    {
        book.CreateLimitOrder(CompanyId1, $"Sell{n}", new OrderValidity.Day(), Side.Sell, 5, price);
        book.CreateLimitOrder(CompanyId2, $"Buy{n}", new OrderValidity.Day(), Side.Buy, 5, price);
    }
}
