using Circus.Actions;
using Circus.Events;
using Circus.MarketData;
using Circus.Sequencing;
using Circus.Sessions;
using Circus.Time;

namespace Circus.Agents;

// Records what a swarm of seeded agents sent, as a trace that can be replayed into a fresh venue.
//
// Agents produce flow in a closed loop - they have to see what the venue did before deciding what
// to send next - and a replay wants the opposite: a list of actions with nothing in the loop at
// all. This is the adapter between the two, and it is what a benchmark measuring the book rather
// than the agents needs, and what a test of Replay needs by definition, since you cannot check
// that a recording reproduces a run using something that regenerates its input.
//
// What comes back is client flow and only client flow. The venue's own opening is not recorded and
// neither are schedule transitions, because how a consumer's books come to be open is its own
// decision - the same one every caller of this already makes for itself.
//
// Pass a seed for a trace that reproduces exactly. Leave it out for a fresh one each run, which is
// what fuzzing wants - though a fuzzing caller should draw its own seed and pass it in, so that
// when something falls over it can say which seed did it.
public static class AgentTrace
{
    // The instant the first recorded action lands on. Fixed rather than read from a clock, since a
    // trace is supposed to reproduce from its seed alone - and it is the same 09:00 the simulator
    // used, so the schedules its consumers built around it still hold.
    private static readonly DateTime Epoch = new(2000, 1, 1, 9, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(1);

    // The recording venue's own schedule, kept where the trace will never reach it: the books are
    // opened directly instead, so the agents have somewhere to quote from the first tick.
    private static readonly MarketSchedule OutOfTheWay =
        new(new TimeSpan(22, 0, 0), new TimeSpan(22, 30, 0), new TimeSpan(23, 30, 0));

    // Agents that never act would otherwise spin here forever waiting for an action count that
    // cannot arrive.
    private const int MaxSilentTicks = 10_000;

    // Chosen for coverage rather than for realism, which is the difference between a trace
    // generator and a market maker. A trace wants every kind of action in it - and in particular
    // wants prints and market orders, which a patient quoting agent would produce very few of.
    private static readonly LiquidityAgentOptions ForTraces = new(
        ActProbability: 0.8,
        Aggression: 0.15,
        MarketOrderProbability: 0.2,
        CancelProbability: 0.15,
        ReplaceProbability: 0.2);

    // Two agents by default, because one alone mostly trades with itself and is prevented from
    // doing so. Their seeds are drawn from the run's, so the whole trace still follows from one
    // number.
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

        // A step behind the epoch, so the first tick lands on it and the first recorded action is
        // stamped there.
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

        // Submitted rather than left to the schedule, and not recorded: it is the venue putting
        // itself where the agents can trade, not a participant doing anything.
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

        // A tick writes as many actions as it likes, so the last one usually overshoots. Trimming
        // from the end is always safe: everything an action refers to was written before it.
        if (trace.Count > actionCount)
            trace.RemoveRange(actionCount, trace.Count - actionCount);

        return trace;
    }

    public static IReadOnlyList<OrderBookAction> Record(Instrument instrument, int actionCount,
        int? seed = null, LiquidityAgentOptions? options = null, int agents = 2) =>
        Record(new[] {instrument}, actionCount, seed, options, agents);

    // Keeps what an agent sent, stamped with the instant the venue is about to stamp it with.
    //
    // Stamped here rather than recorded raw, because a sequencer refuses an action with no time on
    // it - an unstamped recording would be a trace that cannot be replayed. The driver reads the
    // same manual clock at the same instant, so this is the stamp the venue gives it and not a
    // guess at one.
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
