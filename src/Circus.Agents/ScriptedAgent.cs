using Circus.Actions;
using Circus.Events;
using Circus.MarketData;

namespace Circus.Agents;

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

    public IReadOnlyList<OrderBookEvent> OwnEvents => _ownEvents;

    public IReadOnlyList<MarketDataEvent> MarketData => _marketData;

    public int TicksRemaining => _script.Count;

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
