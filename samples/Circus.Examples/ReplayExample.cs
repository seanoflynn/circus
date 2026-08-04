using Circus.Actions;
using Circus.Agents;
using Circus.MarketData;
using Circus.Sequencing;
using Circus.Sessions;

namespace Circus.Examples;

// The backtest shape: a recorded trace in, the market data a subscriber would have seen out.
//
// This is the seam a capture of real venue flow arrives through. A recorded agent run stands in
// for that capture here, but Replay does not care where the actions came from - only that each
// one carries the instant it happened at. No clock is read anywhere in the run, which is what
// makes a replay reproduce a session rather than merely resemble it: a seed gives the same
// trace, and a trace gives the same events, however many times it is run.
//
// The recording is a venue too - agents quoting into real books, watching the feed and being
// told about their own fills. It is only that once it has been written down, nothing of that
// survives except the actions, which is exactly what a capture off the wire would be.
public static class ReplayExample
{
    private static readonly Instrument Bench = new("BENCH", TickSize: 1);

    // A recorded trace starts at 09:00, so an evening session never comes due within it. The
    // schedule is here because a book must have one, not because this sample is about what it
    // does - MarketDataExample covers that.
    private static readonly MarketSchedule OutOfTheWay =
        new(new TimeSpan(22, 0, 0), new TimeSpan(22, 30, 0), new TimeSpan(23, 30, 0));

    public static void Run()
    {
        // Seeded, so this is the same trace every run. Leaving the seed out gives a fresh one
        // each time, which is what fuzzing wants.
        var trace = AgentTrace.Record(Bench, 2_000, seed: 12345);

        var group = new InstrumentGroup(trace[0].Time);
        group.Add(Bench, OutOfTheWay);
        group.Submit(new OpenTrading {Symbol = Bench.Symbol, Time = trace[0].Time});

        var data = Replay.Run(group, trace).Select(m => m.Data).ToList();
        var trades = data.OfType<TradeDataEvent>().ToList();

        Console.WriteLine($"  {trace.Count} actions produced {data.Count} channel messages");
        Console.WriteLine($"  {trades.Count} trades, {trades.Sum(t => t.Quantity)} lots");

        // The last depth message is the book a subscriber would be holding when the trace runs
        // out - rebuilt from the event stream alone, since nothing ever asked the book what it
        // was holding.
        if (data.OfType<LevelsDataEvent>().LastOrDefault() is { } levels)
        {
            Console.WriteLine($"  best bid   {Top(levels.Bids)}");
            Console.WriteLine($"  best offer {Top(levels.Offers)}");
        }
    }

    private static string Top(IReadOnlyList<Level> levels) =>
        levels.Count == 0 ? "(none)" : $"{levels[0].Quantity} @ {levels[0].Price}";
}
