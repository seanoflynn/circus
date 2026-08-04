using Circus.Actions;
using Circus.Events;
using Circus.MarketData;

namespace Circus.Agents;

// An agent that plays a script instead of forming a view: each Enqueue is one tick's worth of
// actions, handed over in order and then nothing.
//
// It exists so the harness can be exercised without a strategy's judgement in the way. What a
// test wants to know about AgentVenue is that events reached the right participant and that
// actions left it in the right order, and neither is easier to see through an agent that is
// deciding things. It is also what a test wanting one specific sequence of orders should reach
// for, rather than tuning a seed until the flow it needs falls out.
//
// It keeps its OrderTracker and MarketView up to date all the same, so what it believes is
// available to assert on - and so the two of them are exercised by every harness test rather than
// only by their own.
public sealed class ScriptedAgent : IAgent
{
    private readonly Queue<IReadOnlyList<OrderBookAction>> _script = new();
    private readonly List<OrderBookEvent> _ownEvents = new();
    private readonly List<MarketDataEvent> _marketData = new();

    public ScriptedAgent(string companyId, params string[] symbols)
    {
        CompanyId = companyId ?? throw new ArgumentNullException(nameof(companyId));
        Symbols = symbols.ToArray();
    }

    public string CompanyId { get; }

    public IReadOnlyList<string> Symbols { get; }

    public OrderTracker Orders { get; } = new();

    public MarketView Market { get; } = new();

    // Everything the venue sent it, in order, for a test that wants to assert on the routing
    // itself rather than on what the agent made of it.
    public IReadOnlyList<OrderBookEvent> OwnEvents => _ownEvents;

    public IReadOnlyList<MarketDataEvent> MarketData => _marketData;

    public int TicksRemaining => _script.Count;

    // One tick's worth. Enqueue nothing to sit out a tick; the script running out is the same as
    // sitting out every tick after it.
    public ScriptedAgent Enqueue(params OrderBookAction[] actions)
    {
        _script.Enqueue(actions.ToArray());
        return this;
    }

    public void OnMarketData(MarketDataEvent data)
    {
        _marketData.Add(data);
        Market.Apply(data);
    }

    public void OnOwnEvent(OrderBookEvent ev)
    {
        _ownEvents.Add(ev);
        Orders.Apply(ev);
    }

    public IReadOnlyList<OrderBookAction> Act(DateTime now) =>
        _script.Count > 0 ? _script.Dequeue() : Array.Empty<OrderBookAction>();
}
