using Circus.Actions;
using Circus.Events;
using Circus.Sequencing;
using Circus.Sessions;
using Circus.Time;
using NUnit.Framework;

namespace Circus.Tests.Sequencing;

// LiveDriver is where wall-clock time enters a running venue and the only place it does. What is
// under test is that it stamps arriving actions rather than trusting them, and that a tick
// dispatches whatever has come due - not the sequencer's ordering, which is tested where it
// lives.
[TestFixture]
public class LiveDriverTests
{
    private static readonly DateTime Day = new(2000, 1, 1);

    private static readonly Instrument Gold = new("GCZ6", 10, 10);

    private static readonly TimeSpan PauseFor = TimeSpan.FromMinutes(2);

    // A 5-tick volatility band on a reference of 100, so a trade at 200 breaches it.
    private static readonly Instrument PausingGold = new("GCZ6", 10, 10,
        PriceRestrictions: new PriceRestrictionConfig[] {new VolatilityBand(5, PauseFor)});

    private static MarketSchedule TradingDay() => new(new(9, 0, 0), new(9, 30, 0), new(17, 0, 0));

    private static MarketSchedule Quiet() => new(new(23, 0, 0), new(23, 15, 0), new(23, 45, 0));

    private static DateTime At(int hour, int minute) => Day.Add(new TimeSpan(hour, minute, 0));

    [Test]
    public void Submit_StampsTheActionWithTheTimeItArrived()
    {
        // arrange
        var clock = new ManualClock(At(12, 0));
        var (driver, _) = Venue(Gold, clock, Quiet());
        driver.Submit(new OpenTrading {Symbol = Gold.Symbol});

        // act - the order arrives a minute later
        clock.SetCurrentTime(At(12, 1));
        driver.Submit(Order("Buy1", Side.Buy, 100));
        var dispatched = driver.Tick();

        // assert
        var order = dispatched.Select(d => d.Action).OfType<CreateLimitOrder>().Single();
        Assert.AreEqual(At(12, 1), order.Time);
    }

    [Test]
    public void Submit_StampsOverATimeTheActionAlreadyCarried()
    {
        // arrange
        var clock = new ManualClock(At(12, 0));
        var (driver, _) = Venue(Gold, clock, Quiet());
        driver.Submit(new OpenTrading {Symbol = Gold.Symbol});

        // act - a client claiming its order arrived an hour ago
        var backdated = Order("Buy1", Side.Buy, 100) with {Time = At(11, 0)};
        driver.Submit(backdated);
        var dispatched = driver.Tick();

        // assert - a participant does not get to say when its order reached the exchange
        var order = dispatched.Select(d => d.Action).OfType<CreateLimitOrder>().Single();
        Assert.AreEqual(At(12, 0), order.Time);
    }

    [Test]
    public void Tick_DispatchesScheduleBoundariesThatHaveComeDue()
    {
        // arrange - a clock starting before the trading day
        var clock = new ManualClock(Day);
        var (driver, book) = Venue(Gold, clock, TradingDay());

        // act
        clock.SetCurrentTime(At(10, 0));
        var dispatched = driver.Tick();

        // assert - stamped at the boundaries themselves, not at whatever the clock read when the
        // tick happened to land
        Assert.AreEqual(2, dispatched.Count);
        Assert.AreEqual(At(9, 0), dispatched[0].Action.Time);
        Assert.AreEqual(At(9, 30), dispatched[1].Action.Time);
        Assert.AreEqual(OrderBookStatus.Open, book.Status);
    }

    [Test]
    public void Tick_NothingDue_DispatchesNothing()
    {
        // arrange
        var clock = new ManualClock(At(12, 0));
        var (driver, _) = Venue(Gold, clock, Quiet());

        // act
        clock.SetCurrentTime(At(12, 5));

        // assert
        Assert.IsEmpty(driver.Tick());
    }

    [Test]
    public void Tick_PastAnInterruptionDeadline_BringsTheBookBack()
    {
        // arrange - trade through the band, so the book pauses for two minutes
        var clock = new ManualClock(At(12, 0));
        var (driver, book) = Venue(PausingGold, clock, Quiet());

        driver.Submit(new OpenTrading {Symbol = PausingGold.Symbol, ReferencePrice = 100});
        clock.SetCurrentTime(At(12, 1));
        driver.Submit(Order("Sell1", Side.Sell, 200));
        driver.Submit(Order("Buy1", Side.Buy, 200));
        driver.Tick();

        Assert.AreEqual(OrderBookStatus.Paused, book.Status, "expected the band to be breached");

        // act - a tick after the deadline, with no client flow to carry it there
        clock.SetCurrentTime(At(12, 4));
        var dispatched = driver.Tick();

        // assert - the poke came from the driver, stamped at the deadline rather than at the tick
        var tick = dispatched.Select(d => d.Action).OfType<AdvanceTime>().Single();
        Assert.AreEqual(At(12, 1) + PauseFor, tick.Time);
        Assert.AreEqual(OrderBookStatus.Open, book.Status);
    }

    [Test]
    public void Tick_TwiceAtTheSameInstant_DispatchesNothingTheSecondTime()
    {
        // arrange
        var clock = new ManualClock(Day);
        var (driver, _) = Venue(Gold, clock, TradingDay());
        clock.SetCurrentTime(At(10, 0));

        // act
        var first = driver.Tick();
        var second = driver.Tick();

        // assert - a host ticking on a timer faster than anything comes due is not a problem
        Assert.IsNotEmpty(first);
        Assert.IsEmpty(second);
    }

    [Test]
    public void Submit_AClockThatWentBackwards_IsRefusedRatherThanReorderingTheVenue()
    {
        // arrange
        var clock = new ManualClock(At(12, 0));
        var (driver, _) = Venue(Gold, clock, Quiet());
        driver.Submit(new OpenTrading {Symbol = Gold.Symbol});
        driver.Tick();

        // act - an NTP correction, or a clock nobody guaranteed was monotonic
        clock.SetCurrentTime(At(11, 59));

        // assert - loud, rather than quietly inserting into a past the venue has dispatched
        Assert.Throws<ArgumentException>(() => driver.Submit(Order("Buy1", Side.Buy, 100)));
    }

    private static (LiveDriver Driver, OrderBook Book) Venue(Instrument instrument, IClock clock,
        MarketSchedule schedule)
    {
        var book = new OrderBook(instrument);
        var sequencer = new Sequencer(clock.GetCurrentTime());
        sequencer.Add(book, schedule);
        return (new LiveDriver(sequencer, clock), book);
    }

    // No time: the driver is the thing that decides that, which is the point.
    private static CreateLimitOrder Order(string clientOrderId, Side side, decimal price) =>
        new()
        {
            Symbol = Gold.Symbol, CompanyId = "Company1", ClientOrderId = clientOrderId,
            OrderValidity = new OrderValidity.Day(), Side = side, Quantity = 5, Price = price
        };
}
