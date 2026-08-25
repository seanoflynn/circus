using Circus.Events;
using Circus.MarketData;
using Circus.Sequencing;
using Circus.Time;

namespace Circus.Agents;

public sealed class AgentSwarm
{
    private readonly InstrumentGroup _group;
    private readonly LiveDriver _driver;

    private readonly List<IAgent> _agents = new();
    private readonly Dictionary<string, List<IAgent>> _byCompany = new();
    private readonly Dictionary<string, List<IAgent>> _bySymbol = new();

    // The driver must be the one pumping this group's sequencer. Nothing here can check that, and
    // a mismatched pair would dispatch one venue while publishing another's feed.
    public AgentSwarm(InstrumentGroup group, LiveDriver driver)
    {
        _group = group ?? throw new ArgumentNullException(nameof(group));
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
    }

    public IReadOnlyList<IAgent> Agents => _agents;

    public void Add(IAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);

        _agents.Add(agent);
        Subscribe(_byCompany, agent.CompanyId, agent);

        foreach (var symbol in agent.Symbols)
            Subscribe(_bySymbol, symbol, agent);
    }

    public IReadOnlyList<ChannelMessage> Tick(DateTime now)
    {
        List<ChannelMessage>? published = null;

        foreach (var dispatched in _driver.Tick())
        {
            foreach (var ev in dispatched.Events)
            {
                if (ev is OrderEvent order && _byCompany.TryGetValue(order.CompanyId, out var owners))
                {
                    foreach (var owner in owners)
                        owner.OnOwnEvent(ev);
                }
            }

            foreach (var message in _group.Channel.Publish(dispatched.Events))
            {
                (published ??= new List<ChannelMessage>()).Add(message);

                if (_bySymbol.TryGetValue(message.Data.Symbol, out var subscribers))
                {
                    foreach (var subscriber in subscribers)
                        subscriber.OnMarketData(message.Data);
                }
            }
        }

        foreach (var agent in _agents)
        {
            foreach (var action in agent.Act(now))
                _driver.Submit(action);
        }

        return published ?? (IReadOnlyList<ChannelMessage>) Array.Empty<ChannelMessage>();
    }

    // The clock must be the one the swarm's driver holds: this steps it and the driver reads it.
    public static void Run(AgentSwarm swarm, ManualClock clock, TimeSpan step, int ticks,
        Action<ChannelMessage>? onPublished = null)
    {
        ArgumentNullException.ThrowIfNull(swarm);
        ArgumentNullException.ThrowIfNull(clock);

        if (step <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(step), step,
                "a step moves the clock forward; time runs one way");

        ArgumentOutOfRangeException.ThrowIfNegative(ticks);

        for (var i = 0; i < ticks; i++)
        {
            var now = clock.GetCurrentTime() + step;
            clock.SetCurrentTime(now);

            foreach (var message in swarm.Tick(now))
                onPublished?.Invoke(message);
        }
    }

    private static void Subscribe(Dictionary<string, List<IAgent>> index, string key, IAgent agent)
    {
        if (!index.TryGetValue(key, out var subscribers))
            index[key] = subscribers = new List<IAgent>();

        subscribers.Add(agent);
    }
}
