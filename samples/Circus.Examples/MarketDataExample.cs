using Circus.Actions;
using Circus.Sequencing;
using Circus.Sessions;

namespace Circus.Examples;

// Two instruments published down one channel, the way a venue does it: one contiguous sequence
// a subscriber counts to notice it has missed something, and every message saying which
// instrument it is about.
//
// InstrumentGroup holds the two halves together - registering an instrument adds it to the
// sequencer and to the channel at once, so the wiring between them cannot be got wrong. Nothing
// here opens a book by hand either: the schedule says when a session begins and the sequencer
// dispatches those transitions in among the order flow, which is why the opening auction below
// prints without anything asking it to.
public static class MarketDataExample
{
    private static readonly Instrument Gold = new("GCZ6", TickSize: 10);
    private static readonly Instrument Silver = new("SIZ6", TickSize: 10);

    private static readonly DateTime Day = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

    private static readonly MarketSchedule Schedule =
        new(new TimeSpan(8, 30, 0), new TimeSpan(9, 0, 0), new TimeSpan(17, 0, 0));

    public static void Run()
    {
        var group = new InstrumentGroup(Day.AddHours(8));
        group.Add(Gold, Schedule);
        group.Add(Silver, Schedule);

        // Orders arriving in pre-open accumulate rather than trading, with the indicative quote
        // following them as they do. The open is what prints them, as one auction at one price.
        var trace = new List<OrderBookAction>
        {
            Order(Gold, "Buyer", "B1", Side.Buy, 3, 1000, Day.AddHours(8).AddMinutes(45)),
            Order(Gold, "Seller", "S1", Side.Sell, 5, 1000, Day.AddHours(8).AddMinutes(46)),
            Order(Silver, "Buyer", "B2", Side.Buy, 2, 1000, Day.AddHours(8).AddMinutes(47)),
            Order(Silver, "Seller", "S2", Side.Sell, 2, 1000, Day.AddHours(8).AddMinutes(48))
        };

        // Advanced past the open, so the schedule's 09:00 transition comes due within the run
        // rather than being left in the queue behind the last action.
        Display.Print(Replay.Run(group, trace, until: Day.AddHours(9).AddMinutes(1)));
    }

    private static CreateLimitOrder Order(Instrument instrument, string companyId, string clientOrderId,
        Side side, int quantity, decimal price, DateTime time) =>
        new()
        {
            Symbol = instrument.Symbol, Time = time, CompanyId = companyId, ClientOrderId = clientOrderId,
            OrderValidity = new OrderValidity.Day(), Side = side, Quantity = quantity, Price = price
        };
}
