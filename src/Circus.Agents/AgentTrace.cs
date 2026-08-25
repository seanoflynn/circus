using Circus.Actions;
using Circus.Events;
using Circus.MarketData;
using Circus.Sequencing;
using Circus.Sessions;
using Circus.Time;

namespace Circus.Agents;

public static class AgentTrace
{
    private static readonly DateTime Epoch = new(2000, 1, 1, 9, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(1);

    private static readonly MarketSchedule OutOfTheWay =
        new(new TimeSpan(22, 0, 0), new TimeSpan(22, 30, 0), new TimeSpan(23, 30, 0));

    private const int MaxSilentTicks = 10_000;

    private static readonly LiquidityAgentOptions ForTraces = new(
        ActProbability: 0.8,
        Aggression: 0.15,
        MarketOrderProbability: 0.2,
        CancelProbability: 0.15,
        ReplaceProbability: 0.2);

    public static IReadOnlyList<OrderBookAction> Record(IReadOnlyList<Instrument> instruments,
        int actionCount, int? seed = null, LiquidityAgentOptions? options = null, int agents = 2)
    {
        ArgumentNullException.ThrowIfNull(instruments);
        ArgumentOutOfRangeException.ThrowIfNegative(actionCount);

        if (instruments.Count == 0)
            throw new ArgumentException("at least one instrument is required", nameof(instruments));
        if (agents < 1)
            throw new ArgumentException("at least one agent is required", nameof(agents));

        var trace = new List<OrderBookAction>(actionCount);
        if (actionCount == 0) return trace;

        var clock = new ManualClock(Epoch - Step);
        var group = new InstrumentGroup(clock.GetCurrentTime());

        foreach (var instrument in instruments)
            group.Add(instrument, OutOfTheWay);

        var driver = new LiveDriver(group.Sequencer, clock);
        var swarm = new AgentSwarm(group, driver);

        var seeds = new Random(seed ?? Random.Shared.Next());
        for (var i = 0; i < agents; i++)
        {
            var agent = new LiquidityAgent($"A{i}", instruments, options ?? ForTraces, seeds.Next());
            swarm.Add(new Recorded(agent, trace));
        }

        foreach (var instrument in instruments)
            driver.Submit(new OpenTrading {Symbol = instrument.Symbol});

        var silent = 0;
        while (trace.Count < actionCount)
        {
            var before = trace.Count;
            AgentSwarm.Run(swarm, clock, Step, 1);

            if (trace.Count > before)
            {
                silent = 0;
                continue;
            }

            if (++silent > MaxSilentTicks)
                throw new InvalidOperationException(
                    $"the agents produced {trace.Count} of the {actionCount} actions asked for and then " +
                    $"went quiet for {MaxSilentTicks} ticks. An agent that never acts - ActProbability " +
                    "of zero, a position limit leaving it no side to add to, or a book it never sees " +
                    "open - cannot fill a trace.");
        }

        if (trace.Count > actionCount)
            trace.RemoveRange(actionCount, trace.Count - actionCount);

        return trace;
    }

    public static IReadOnlyList<OrderBookAction> Record(Instrument instrument, int actionCount,
        int? seed = null, LiquidityAgentOptions? options = null, int agents = 2) =>
        Record(new[] {instrument}, actionCount, seed, options, agents);

    private sealed class Recorded : IAgent
    {
        private readonly IAgent _inner;
        private readonly List<OrderBookAction> _trace;

        public Recorded(IAgent inner, List<OrderBookAction> trace)
        {
            _inner = inner;
            _trace = trace;
        }

        public string CompanyId => _inner.CompanyId;

        public IReadOnlyList<string> Symbols => _inner.Symbols;

        public void OnMarketData(MarketDataEvent data) => _inner.OnMarketData(data);

        public void OnOwnEvent(OrderBookEvent ev) => _inner.OnOwnEvent(ev);

        public IReadOnlyList<OrderBookAction> Act(DateTime now)
        {
            var actions = _inner.Act(now);

            foreach (var action in actions)
                _trace.Add(action with {Time = now});

            return actions;
        }
    }
}
