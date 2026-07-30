using BenchmarkDotNet.Attributes;
using Circus.Actions;
using Circus.Sequencing;
using Circus.Sessions;
using Circus.Simulator;

namespace Circus.Benchmarks;

// Replays a fixed, seeded trace of realistic order flow against a fresh order book each
// invocation. This is the headline number: throughput/allocations for a whole session's
// worth of activity, rather than any single operation in isolation.
[MemoryDiagnoser]
public class OrderBookThroughputBenchmarks
{
    [Params(1_000, 10_000, 100_000)]
    public int ActionCount;

    private Security _security = null!;
    private IReadOnlyList<OrderBookAction> _trace = null!;

    // The trace runs from 09:00, so an evening session never comes due within it.
    private static readonly MarketSchedule OutOfTheWay =
        new(new TimeSpan(22, 0, 0), new TimeSpan(22, 30, 0), new TimeSpan(23, 30, 0));

    [GlobalSetup]
    public void Setup()
    {
        _security = new Security("BENCH", SecurityType.Future, 1m, 10m);
        var simulator = new OrderFlowSimulator(_security, seed: 12345);
        _trace = simulator.Generate(ActionCount);
    }

    [Benchmark(Baseline = true)]
    public int ReplayTrace()
    {
        // No clock: the trace carries the time each action happened, so replaying it is the
        // whole job. Opening is stamped at the first action's instant - the book refuses time
        // running backwards, and equal stamps are fine.
        var book = new OrderBook(_security);
        book.Process(new OpenTrading {Security = _security, Time = _trace[0].Time});

        var eventCount = 0;
        foreach (var action in _trace)
            eventCount += book.Process(action).Count;

        return eventCount;
    }

    // The same trace and the same book, reached through the queue that decides what order things
    // happened in. Against the baseline above, the difference is what sequencing costs: a
    // priority-queue insert and pop per action, a routing lookup, and a scan of each action's
    // events for an interruption deadline.
    //
    // Worth having as a number rather than an assumption, since every action in a running venue
    // pays it.
    [Benchmark]
    public int ReplayTraceThroughSequencer()
    {
        var book = new OrderBook(_security);
        var sequencer = new Sequencer(_trace[0].Time);

        // A schedule whose boundaries fall outside the trace, so what is measured is the queue
        // rather than transitions the baseline never dispatched. Opening is submitted instead,
        // matching the baseline action for action.
        sequencer.Add(book, OutOfTheWay);
        sequencer.Submit(new OpenTrading {Security = _security, Time = _trace[0].Time});

        var eventCount = 0;
        Replay.Run(sequencer, _trace, d => eventCount += d.Events.Count);

        return eventCount;
    }
}
