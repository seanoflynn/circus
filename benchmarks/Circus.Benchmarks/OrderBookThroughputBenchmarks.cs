using BenchmarkDotNet.Attributes;
using Circus.Actions;
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

    [GlobalSetup]
    public void Setup()
    {
        _security = new Security("BENCH", SecurityType.Future, 1m, 10m);
        var simulator = new OrderFlowSimulator(_security, seed: 12345);
        _trace = simulator.Generate(ActionCount);
    }

    [Benchmark]
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
}
