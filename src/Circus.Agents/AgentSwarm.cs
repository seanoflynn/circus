using Circus.Events;
using Circus.MarketData;
using Circus.Sequencing;
using Circus.Time;

namespace Circus.Agents;

// A population of participants trading at a venue that already exists.
//
// The venue is not this class's to build. An InstrumentGroup holding the books and their
// schedules, and the LiveDriver pumping its sequencer off a clock, are wired by whoever owns the
// venue - which is what a host does anyway, and is what lets the same group carry restrictions,
// matching algorithms and instruments the agents know nothing about, alongside flow from gateways
// that are not agents at all. A swarm is the participants and the return path to them, and
// nothing else.
//
//     tick:  dispatch what has come due
//            for each dispatch: own events to the agent they belong to, then the channel's
//                               messages to everyone subscribed to that instrument
//            then, in registration order, ask each agent what it wants to send
//
// No clock is read here. `now` is passed in by whoever owns one, the way a book is told the
// instant an action happened rather than looking it up - and what the agents send is stamped by
// the driver on its own reading, so the swarm never has to agree with it about anything beyond
// order.
//
// Orders sent on a tick are dispatched on the next one. That single tick of latency is what makes
// the loop well founded rather than re-entrant - an agent never sees the consequences of its own
// order inside the call that placed it - and it is what a participant experiences anyway.
//
// Own events reach an agent before the public messages for the same dispatch, which is the order
// a real participant sees them in: a fill is reported to the party to it before the print reaches
// the feed.
//
// The driver's clock decides what kind of run this is, and nothing here changes. A ManualClock
// stepped by Run gives a run that reproduces exactly - the shape tests and recorded traces want.
// A SystemClock with Tick pumped on a timer gives a live venue that agents quote into and
// anything else can trade against by submitting to the same driver.
//
// Single-threaded, like everything it attaches to.
public sealed class AgentSwarm
{
    private readonly InstrumentGroup _group;
    private readonly LiveDriver _driver;

    private readonly List<IAgent> _agents = new();
    private readonly Dictionary<string, List<IAgent>> _byCompany = new();
    private readonly Dictionary<string, List<IAgent>> _bySymbol = new();

    // The driver must be the one pumping this group's sequencer. Nothing here can check that -
    // a driver does not expose the sequencer it drives - and a mismatched pair would dispatch
    // one venue while publishing another's feed.
    public AgentSwarm(InstrumentGroup group, LiveDriver driver)
    {
        _group = group ?? throw new ArgumentNullException(nameof(group));
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
    }

    // In registration order, which is the order they are asked to act in.
    public IReadOnlyList<IAgent> Agents => _agents;

    // Agents may share a company id - one firm, two desks - and each then sees the other's order
    // events, the way a firm's drop copy carries the whole firm rather than one desk of it. It is
    // also what makes self-match prevention reachable, since the book keys orders by company.
    public void Add(IAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);

        _agents.Add(agent);
        Subscribe(_byCompany, agent.CompanyId, agent);

        foreach (var symbol in agent.Symbols)
            Subscribe(_bySymbol, symbol, agent);
    }

    // Dispatches everything the driver's clock says is due, routes what came back, and submits
    // whatever the agents want to send. Returns the channel's messages for this tick, so a host
    // can publish them onward - a subscriber outside the venue sees exactly what the agents saw.
    //
    // `now` is what the agents are told the time is. A host passes its own clock's reading, which
    // is the same one the driver is about to dispatch and stamp against.
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

    // A run with no wall clock in it: step the clock, tick, repeat. The counterpart to Replay for
    // a venue whose flow does not exist yet - a trace is submitted and advanced through, whereas
    // agents have to be given the time in which to produce one.
    //
    // The clock passed must be the one the swarm's driver holds, since this steps that clock and
    // the driver reads it. Deterministic given deterministic agents, which is what a seeded one
    // is: the same seeds and the same step produce the same run, message for message.
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
