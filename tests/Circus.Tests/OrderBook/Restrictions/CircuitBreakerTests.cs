using Circus.OrderBook;
using Circus.OrderBook.Actions;
using Circus.OrderBook.Events;
using Circus.Time;
using NUnit.Framework;

namespace Circus.Tests.OrderBook.Restrictions;

// Circuit breakers stop trading rather than capping it, and are configured in levels. What
// matters beyond the halt itself is that a price through several levels is served by the one it
// actually reached rather than by whichever it passed first.
[TestFixture]
public class CircuitBreakerTests
{
    private static readonly DateTime Now1 = new(2000, 1, 1, 12, 0, 0);
    private static readonly TimeSpan HaltFor = TimeSpan.FromMinutes(15);

    private static readonly string CompanyId1 = "Company1";
    private static readonly string CompanyId2 = "Company2";

    private ManualClock Clock;

    [SetUp]
    public void SetUp()
    {
        Clock = new ManualClock(Now1);
    }

    // CME's equity index levels: halt at 7 and 13 percent, end the trading day at 20. On a
    // reference of 1000 with a tick size of 10 that is 7, 13 and 20 ticks either side.
    private IOrderBook LeveledBook()
    {
        var security = new Security("GCZ6", SecurityType.Future, 10, 10,
            PriceRestrictions: new PriceRestrictionConfig[]
            {
                new CircuitBreaker(new PriceLimitWidth.Percent(7), HaltFor),
                new CircuitBreaker(new PriceLimitWidth.Percent(13), HaltFor),
                new CircuitBreaker(new PriceLimitWidth.Percent(20))
            });
        var book = new InMemoryOrderBook(security, Clock);
        book.UpdateStatus(OrderBookStatus.Open, 1000);
        return book;
    }

    private static void Trade(IOrderBook book, string n, decimal price)
    {
        book.CreateLimitOrder(CompanyId1, $"Sell{n}", new OrderValidity.Day(), Side.Sell, 5, price);
        book.CreateLimitOrder(CompanyId2, $"Buy{n}", new OrderValidity.Day(), Side.Buy, 5, price);
    }

    [Test]
    public void FirstLevelBreached_HaltsForItsDuration()
    {
        // arrange
        var book = LeveledBook();

        // act - 1080 is 8 percent away, through the first level and no further
        Trade(book, "1", 1080);

        // assert
        Assert.AreEqual(OrderBookStatus.Halted, book.Status);

        // the orders that tripped it are pulled, so resuming has nothing left to re-trip on
        book.CancelOrder(CompanyId1, "Cancel1", "Sell1");
        book.CancelOrder(CompanyId2, "Cancel2", "Buy1");

        Clock.SetCurrentTime(Now1 + HaltFor);
        var events = book.AdvanceTime();

        Assert.AreEqual(OrderBookStatus.Open, book.Status);
        Assert.AreEqual(StatusChangeReason.InterruptionElapsed,
            events.OfType<StatusChanged>().Single().Reason);
    }

    [Test]
    public void PriceThroughEveryLevel_ServedByTheWidestOneItReached()
    {
        // arrange
        var book = LeveledBook();

        // act - 1250 is 25 percent away, through all three levels
        Trade(book, "1", 1250);
        Assert.AreEqual(OrderBookStatus.Halted, book.Status);

        // act - the two narrower levels would each have ended by now
        book.CancelOrder(CompanyId1, "Cancel1", "Sell1");
        book.CancelOrder(CompanyId2, "Cancel2", "Buy1");
        Clock.SetCurrentTime(Now1 + HaltFor + HaltFor);
        var events = book.AdvanceTime();

        // assert - still halted, because the level it actually reached never resumes on its own
        Assert.AreEqual(OrderBookStatus.Halted, book.Status);
        Assert.AreEqual(0, events.Count);
    }

    [Test]
    public void HaltOutranksAPauseBreachedAtTheSamePrice()
    {
        // arrange - a volatility band far narrower than the breaker, so any price tripping the
        // breaker trips the band too. The severer consequence has to win.
        var security = new Security("GCZ6", SecurityType.Future, 10, 10,
            PriceRestrictions: new PriceRestrictionConfig[]
            {
                new VolatilityBand(2, PauseFor: TimeSpan.FromMinutes(2)),
                new CircuitBreaker(new PriceLimitWidth.Percent(7), HaltFor)
            });
        var book = new InMemoryOrderBook(security, Clock);
        book.UpdateStatus(OrderBookStatus.Open, 1000);

        // act
        Trade(book, "1", 1080);

        // assert
        Assert.AreEqual(OrderBookStatus.Halted, book.Status, "halting outranks pausing");
    }

    [Test]
    public void OrderEntryIsUnaffected_ABreakerGovernsTradesOnly()
    {
        // arrange
        var book = LeveledBook();

        // act - far beyond every level, but resting an order there is not trading there
        var events = book.CreateLimitOrder(CompanyId1, "Order1", new OrderValidity.Day(), Side.Buy, 5, 5000);

        // assert
        Assert.IsInstanceOf<CreateOrderConfirmed>(events[0]);
        Assert.AreEqual(OrderBookStatus.Open, book.Status);
    }
}
