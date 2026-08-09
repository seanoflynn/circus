using Circus.Actions;
using Circus.MarketData;
using Circus.Sequencing;
using Circus.Sessions;

namespace Circus.Examples;

// One session, published twice: once the way CME shapes a feed and once the way Eurex does.
//
// CME runs a channel per product group carrying every product about every instrument on it, so a
// subscriber takes one channel and has the whole complex. Eurex publishes the same book on two
// interfaces - EOBI order by order, EMDI netted depth with the prints - so a participant that
// wants both subscribes twice, and each interface restates itself at its own rate.
//
// Neither is built into the library. Both are the same three calls with different arguments,
// which is the point: the shape of a venue is configuration here rather than code.
public static class VenueShapesExample
{
    private static readonly Instrument Gold = new("GCZ6", TickSize: 10);
    private static readonly Instrument Silver = new("SIZ6", TickSize: 10);

    private static readonly DateTime Day = new(2026, 1, 5, 8, 0, 0, DateTimeKind.Utc);

    private static readonly MarketSchedule Schedule =
        new(new TimeSpan(8, 30, 0), new TimeSpan(9, 0, 0), new TimeSpan(17, 0, 0));

    private static readonly TimeSpan SnapshotInterval = TimeSpan.FromMinutes(10);

    public static void Run()
    {
        Describe("CME-shaped: one channel per product group", CmeShaped());
        Console.WriteLine();
        Describe("Eurex-shaped: one book, two interfaces", EurexShaped());
    }

    private static InstrumentGroup CmeShaped()
    {
        var group = new InstrumentGroup(Day, SnapshotInterval);

        group.AddChannel("310", FeedProducts.All);
        group.Add(Gold, Schedule);
        group.Add(Silver, Schedule);

        return group;
    }

    private static InstrumentGroup EurexShaped()
    {
        var group = new InstrumentGroup(Day, SnapshotInterval);

        // The order-by-order interface carries the whole book, so its image is the heaviest thing
        // published here and cycles once every six ticks rather than on all of them.
        group.AddChannel("EOBI", FeedProducts.ByOrder | FeedProducts.Status, snapshotEvery: 6);
        group.AddChannel("EMDI",
            FeedProducts.ByPrice | FeedProducts.Trades | FeedProducts.Status | FeedProducts.Indicative);

        group.Add(Gold, Schedule);
        group.Add(Silver, Schedule);

        return group;
    }

    // Counts rather than a dump: what differs between the two shapes is which messages reach
    // which subscriber, and a few hundred lines of them would bury that rather than show it.
    private static void Describe(string title, InstrumentGroup group)
    {
        Console.WriteLine($"  {title}");

        var published = Replay.RunAll(group, Trace(), Day.AddHours(2));

        foreach (var name in group.ChannelNames)
        {
            var messages = published[name];
            var channel = group.ChannelNamed(name);

            Console.WriteLine($"    {name} [{string.Join(", ", channel.Symbols)}]");

            foreach (var stream in new[] {ChannelStream.Incremental, ChannelStream.Snapshot})
            {
                var counts = messages.Where(m => m.Stream == stream)
                    .GroupBy(m => m.Data.GetType().Name)
                    .OrderBy(g => g.Key)
                    .Select(g => $"{g.Key} x{g.Count()}")
                    .ToList();

                Console.WriteLine(counts.Count == 0
                    ? $"      {stream,-11} -"
                    : $"      {stream,-11} {string.Join(", ", counts)}");
            }
        }
    }

    // Enough flow to move every product: an opening auction, depth on both sides, an aggressor
    // through three levels, and a second instrument so a channel carrying a group has two.
    private static IReadOnlyList<OrderBookAction> Trace() =>
        new List<OrderBookAction>
        {
            Order(Gold, "Buyer", "B-open", Side.Buy, 5, 1000, At(8, 45)),
            Order(Gold, "Seller", "S-open", Side.Sell, 3, 1000, At(8, 46)),
            Order(Gold, "Maker", "B1", Side.Buy, 4, 990, At(9, 5)),
            Order(Gold, "Maker", "B2", Side.Buy, 3, 980, At(9, 6)),
            Order(Gold, "Maker", "S1", Side.Sell, 4, 1010, At(9, 7)),
            Order(Gold, "Maker", "S2", Side.Sell, 3, 1020, At(9, 8)),
            Order(Gold, "Maker", "S3", Side.Sell, 5, 1030, At(9, 9)),
            Order(Gold, "Taker", "T1", Side.Buy, 11, 1030, At(9, 15)),
            Order(Silver, "Maker", "SB1", Side.Buy, 3, 490, At(9, 20)),
            Order(Silver, "Taker", "ST1", Side.Sell, 3, 490, At(9, 21))
        };

    private static DateTime At(int hour, int minute) =>
        new(Day.Year, Day.Month, Day.Day, hour, minute, 0, DateTimeKind.Utc);

    private static CreateLimitOrder Order(Instrument instrument, string companyId, string clientOrderId,
        Side side, int quantity, decimal price, DateTime time) =>
        new()
        {
            Symbol = instrument.Symbol, Time = time, CompanyId = companyId, ClientOrderId = clientOrderId,
            OrderValidity = new OrderValidity.Day(), Side = side, Quantity = quantity, Price = price
        };
}
