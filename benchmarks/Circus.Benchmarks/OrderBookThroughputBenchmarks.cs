using BenchmarkDotNet.Attributes;
using Circus.Actions;
using Circus.Simulator;
using Circus.Time;

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
        var book = new OrderBook(_security, new ManualClock(DateTime.UtcNow));
        book.UpdateStatus(OrderBookStatus.Open);

        var eventCount = 0;
        foreach (var action in _trace)
            eventCount += book.Process(action).Count;

        return eventCount;
    }
}
