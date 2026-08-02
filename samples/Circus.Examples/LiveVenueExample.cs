using Circus.Actions;
using Circus.Sequencing;
using Circus.Sessions;
using Circus.Time;

namespace Circus.Examples;

// A venue driven off a clock rather than off a trace.
//
// LiveDriver is the only part of a running venue that reads one. Submit stamps an arriving
// action with the time it arrived - the way a gateway stamps an inbound message and the matching
// engine then works off that stamp rather than off whatever the clock reads by the time it
// reaches the message - and Tick dispatches whatever has come due since the last one: order
// flow, schedule boundaries and interruptions due back, all through the single queue.
//
// One thread. The sequencer, the books and the channel are each single-threaded by construction,
// so a host pumps Tick on the same thread it submits on, and a gateway on an I/O thread hands
// work across to that thread rather than calling in.
//
// The clock is a ManualClock, which is also what a test wanting a venue it can hold still would
// use: the same shape as the SystemClock a production host passes, with the nondeterminism taken
// out. Nothing else in the pipeline has to change to get one.
public static class LiveVenueExample
{
    private static readonly Instrument Gold = new("GCZ6", TickSize: 10);

    private static readonly DateTime Day = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

    private static readonly MarketSchedule Schedule =
        new(new TimeSpan(8, 30, 0), new TimeSpan(9, 0, 0), new TimeSpan(17, 0, 0));

    public static void Run()
    {
        var clock = new ManualClock(Day.AddHours(8));

        var group = new InstrumentGroup(clock.GetCurrentTime());
        group.Add(Gold, Schedule);

        var driver = new LiveDriver(group.Sequencer, clock);

        // Nothing has come due: the book is closed and the schedule's first boundary is still
        // half an hour out. A host ticks anyway, because it cannot know that.
        Pump(driver, group);

        // Pre-open arrives from the schedule rather than from anything submitted here.
        clock.SetCurrentTime(Day.AddHours(8).AddMinutes(30));
        Pump(driver, group);

        // Neither order carries a time. The driver stamps both with the clock, because a
        // participant does not get to say when its order reached the exchange.
        driver.Submit(Order("Buyer", "B1", Side.Buy, 3, 1000));
        driver.Submit(Order("Seller", "S1", Side.Sell, 5, 1000));
        Pump(driver, group);

        // The open, and with it the auction print the pre-open had been quoting.
        clock.SetCurrentTime(Day.AddHours(9));
        Pump(driver, group);
    }

    private static void Pump(LiveDriver driver, InstrumentGroup group)
    {
        foreach (var dispatched in driver.Tick())
            Display.Print(group.Channel.Publish(dispatched.Events));
    }

    private static CreateLimitOrder Order(string companyId, string clientOrderId, Side side,
        int quantity, decimal price) =>
        new()
        {
            Symbol = Gold.Symbol, CompanyId = companyId, ClientOrderId = clientOrderId,
            OrderValidity = new OrderValidity.Day(), Side = side, Quantity = quantity, Price = price
        };
}
