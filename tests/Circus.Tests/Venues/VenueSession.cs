using Circus.Actions;
using Circus.MarketData;
using Circus.Sequencing;
using Circus.Sessions;

namespace Circus.Tests.Venues;

// One session, driven through whatever shape a venue is configured in. Every test in this folder
// runs this same trace, so what differs between two venues is the configuration and nothing else -
// which is the point being made, and would not be made by two traces that happened to agree.
//
// The trace is deliberately not quiet. It opens on an auction, builds depth on both sides, sweeps
// three levels with one aggressor, empties a level with a cancel, and rests an iceberg that is
// partly filled - so a shape that only works for a resting limit order fails here rather than in
// whatever uses it next.
internal static class VenueSession
{
    // Ten a tick, so every price below is a round number of them and a rejected order cannot
    // quietly turn an assertion about depth into an assertion about nothing.
    public static readonly Instrument Gold = new("GCZ6", TickSize: 10);
    public static readonly Instrument Silver = new("SIZ6", TickSize: 10);

    public static readonly DateTime Day = new(2026, 1, 5, 8, 0, 0, DateTimeKind.Utc);

    public static readonly MarketSchedule Schedule =
        new(new TimeSpan(8, 30, 0), new TimeSpan(9, 0, 0), new TimeSpan(17, 0, 0));

    // Past the last action, so a snapshot cycle comes round a few more times after the flow stops
    // and a shape whose channels restate at different rates has something to differ over.
    public static readonly DateTime Until = Day.AddHours(2);

    // How deep the by-price products here run, which is not a choice either shape makes: every
    // channel publishes the venue's one window. Ten is what CME's futures books carry and what
    // Eurex's netted depth feed publishes anyway, so the difference between the two shapes was
    // always elsewhere.
    public const int Depth = OrderBook.PublishedDepth;

    public static IReadOnlyList<OrderBookAction> Trace()
    {
        var trace = new List<OrderBookAction>();

        // Pre-open. Orders accumulate rather than trading, and the indicative quote follows them.
        trace.Add(Limit(Gold, "Buyer", "B-open", Side.Buy, 5, 1000, At(8, 45)));
        trace.Add(Limit(Gold, "Seller", "S-open", Side.Sell, 3, 1000, At(8, 46)));
        trace.Add(Limit(Silver, "Buyer", "B-ag", Side.Buy, 2, 500, At(8, 47)));
        trace.Add(Limit(Silver, "Seller", "S-ag", Side.Sell, 2, 500, At(8, 48)));

        // 09:00 opens on the schedule, printing the crossed pre-open book as one auction.

        // Continuous. Four levels a side on Gold, so a sweep has something to sweep and a
        // ten-deep window has something inside it.
        trace.Add(Limit(Gold, "Maker", "B1", Side.Buy, 4, 990, At(9, 5)));
        trace.Add(Limit(Gold, "Maker", "B2", Side.Buy, 3, 980, At(9, 6)));
        trace.Add(Limit(Gold, "Maker", "B3", Side.Buy, 2, 970, At(9, 7)));
        trace.Add(Limit(Gold, "Maker", "B4", Side.Buy, 6, 960, At(9, 8)));
        trace.Add(Limit(Gold, "Maker", "S1", Side.Sell, 4, 1010, At(9, 9)));
        trace.Add(Limit(Gold, "Maker", "S2", Side.Sell, 3, 1020, At(9, 10)));
        trace.Add(Limit(Gold, "Maker", "S3", Side.Sell, 5, 1030, At(9, 11)));

        // A second order at a price that already has one, so a level carries a queue and its
        // aggregate count is something other than one.
        trace.Add(Limit(Gold, "Other", "B1b", Side.Buy, 2, 990, At(9, 12)));

        // An aggressor through three offer levels in one action. Every product has to report this
        // as one thing: three levels moved, several orders filled, prints at three prices.
        trace.Add(Limit(Gold, "Taker", "T1", Side.Buy, 11, 1030, At(9, 15)));

        // A cancel that empties a level rather than thinning it.
        trace.Add(new CancelOrder
        {
            Symbol = Gold.Symbol, Time = At(9, 20), CompanyId = "Maker", ClientOrderId = "B4-cancel",
            PreviousClientOrderId = "B4"
        });

        // An iceberg, and an aggressor taking exactly its peak. In continuous trading an exhausted
        // peak requeues with a fresh id, which a by-order feed reports and a by-price feed does
        // not - the level is the same size either way. That divergence is a real one and both
        // shapes here have to survive it.
        trace.Add(new CreateLimitOrder
        {
            Symbol = Gold.Symbol, Time = At(9, 25), CompanyId = "Maker", ClientOrderId = "ICE",
            OrderValidity = new OrderValidity.Day(), Side = Side.Sell, Quantity = 12, Price = 1040,
            MaxVisibleQuantity = 3
        });
        trace.Add(Limit(Gold, "Taker", "T2", Side.Buy, 3, 1040, At(9, 26)));

        // Silver, so a channel carrying a group rather than a product has two instruments to
        // interleave and a channel carrying one product has something to leave out.
        trace.Add(Limit(Silver, "Maker", "SB1", Side.Buy, 3, 490, At(9, 30)));
        trace.Add(Limit(Silver, "Taker", "ST1", Side.Sell, 3, 490, At(9, 31)));

        return trace;
    }

    public static IReadOnlyDictionary<string, IReadOnlyList<ChannelMessage>> Run(InstrumentGroup group) =>
        Replay.RunAll(group, Trace(), Until);

    private static DateTime At(int hour, int minute) =>
        new(Day.Year, Day.Month, Day.Day, hour, minute, 0, DateTimeKind.Utc);

    private static CreateLimitOrder Limit(Instrument instrument, string companyId, string clientOrderId,
        Side side, int quantity, decimal price, DateTime time) =>
        new()
        {
            Symbol = instrument.Symbol, Time = time, CompanyId = companyId, ClientOrderId = clientOrderId,
            OrderValidity = new OrderValidity.Day(), Side = side, Quantity = quantity, Price = price
        };
}
