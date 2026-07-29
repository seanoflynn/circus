using Circus.OrderBook;
using Circus.TimeProviders;
using NUnit.Framework;

namespace Circus.Tests.OrderBook;

// What a velocity limit catches that a range around the last trade does not: a run of steps
// each unremarkable next to the one before it, arriving too quickly. The same steps spread out
// are ordinary trading, so every test here turns on timing rather than on price.
[TestFixture]
public class InMemoryOrderBookVelocityLogicTests
{
    private static readonly DateTime Now1 = new(2000, 1, 1, 12, 0, 0);
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    private static readonly string CompanyId1 = "Company1";
    private static readonly string CompanyId2 = "Company2";

    private TestTimeProvider TimeProvider;

    [SetUp]
    public void SetUp()
    {
        TimeProvider = new TestTimeProvider(Now1);
    }

    // 5 ticks on a tick size of 10, so 50 of price movement inside ten seconds is too fast.
    private IOrderBook VelocityBook(TimeSpan? pauseFor = null) =>
        new InMemoryOrderBook(
            new Security("GCZ6", SecurityType.Future, 10, 10,
                PriceRestrictions: new PriceRestrictionConfig[]
                {
                    new VelocityLimit(5, Window, pauseFor)
                }),
            TimeProvider);

    [Test]
    public void StepsArrivingTooQuickly_TripTheLimit()
    {
        // arrange
        var book = VelocityBook();
        book.UpdateStatus(OrderBookStatus.Open);

        // act + assert - nothing has traded, so the first print is unmeasurable and allowed
        Trade(book, 1, 100);
        Assert.AreEqual(OrderBookStatus.Open, book.Status);

        // two seconds later, 40 from the first trade and inside the range
        TimeProvider.SetCurrentTime(Now1.AddSeconds(2));
        Trade(book, 2, 140);
        Assert.AreEqual(OrderBookStatus.Open, book.Status);

        // two seconds later again. This step is only 40 from the last trade, but the trade at
        // 100 is still inside the window and 80 away, which is what the limit is watching for
        TimeProvider.SetCurrentTime(Now1.AddSeconds(4));
        Trade(book, 3, 180);
        Assert.AreEqual(OrderBookStatus.Paused, book.Status);
    }

    [Test]
    public void TheSameStepsSpreadOut_DoNot()
    {
        // arrange - identical prices to the test above, twenty seconds apart instead of two
        var book = VelocityBook();
        book.UpdateStatus(OrderBookStatus.Open);

        // act
        Trade(book, 1, 100);

        TimeProvider.SetCurrentTime(Now1.AddSeconds(20));
        Trade(book, 2, 140);

        TimeProvider.SetCurrentTime(Now1.AddSeconds(40));
        Trade(book, 3, 180);

        // assert - by the third print the first has long left the window, so nothing measures
        // the whole 80 of movement and the market simply traded
        Assert.AreEqual(OrderBookStatus.Open, book.Status);
    }

    [Test]
    public void CatchesWhatAWideRangeAroundTheLastTradeMisses()
    {
        // arrange - a wide volatility band with no window of its own alongside the velocity
        // limit. The band is four times the width, so nothing below comes close to it.
        var security = new Security("GCZ6", SecurityType.Future, 10, 10,
            PriceRestrictions: new PriceRestrictionConfig[]
            {
                new VolatilityBand(20),
                new VelocityLimit(5, Window)
            });
        var book = new InMemoryOrderBook(security, TimeProvider);
        book.UpdateStatus(OrderBookStatus.Open);

        // act
        Trade(book, 1, 100);
        TimeProvider.SetCurrentTime(Now1.AddSeconds(2));
        Trade(book, 2, 140);
        TimeProvider.SetCurrentTime(Now1.AddSeconds(4));
        Trade(book, 3, 180);

        // assert - every step was 40, well inside the band's 200, so the band never had an
        // opinion. Going too fast is the only thing wrong here.
        Assert.AreEqual(OrderBookStatus.Paused, book.Status);
    }

    [Test]
    public void PauseIsBriefAndEndsOnItsOwn()
    {
        // arrange - velocity pauses are measured in seconds, not minutes
        var pauseFor = TimeSpan.FromSeconds(5);
        var book = VelocityBook(pauseFor);
        book.UpdateStatus(OrderBookStatus.Open);

        Trade(book, 1, 100);
        TimeProvider.SetCurrentTime(Now1.AddSeconds(2));
        Trade(book, 2, 140);
        TimeProvider.SetCurrentTime(Now1.AddSeconds(4));
        Trade(book, 3, 180);
        Assert.AreEqual(OrderBookStatus.Paused, book.Status);

        // act - the orders that tripped it are still crossed, so ending the pause prints them
        TimeProvider.SetCurrentTime(Now1.AddSeconds(4) + pauseFor);
        var events = book.AdvanceTime();

        // assert
        Assert.AreEqual(OrderBookStatus.Open, book.Status);
        var matched = events.OfType<OrdersMatched>().Single();
        Assert.AreEqual(180, matched.Price);
    }

    private void Trade(IOrderBook book, int n, decimal price)
    {
        book.CreateLimitOrder(CompanyId1, $"Sell{n}", new OrderValidity.Day(), Side.Sell, 5, price);
        book.CreateLimitOrder(CompanyId2, $"Buy{n}", new OrderValidity.Day(), Side.Buy, 5, price);
    }
}
