using Circus.Actions;
using Circus.Events;
using Circus.MarketData;
using Circus.Sequencing;
using Circus.Sessions;
using Circus.Time;

namespace Circus.Agents;

// A venue with participants in it: an InstrumentGroup, the LiveDriver that stamps and pumps it,
// and the agents trading at it.
//
// It introduces nothing the engine did not already have. Time still comes from one place, the
// driver's clock; dispatch order is still the sequencer's; market data is still the channel's.
// What is added is the return path - taking what the venue published and handing each agent the
// part it is entitled to - and that is the whole of it.
//
//     tick:  dispatch what has come due
//            for each dispatch: own events to the agent they belong to, then the channel's
//                               messages to everyone subscribed to that instrument
//            then, in registration order, ask each agent what it wants to send
//
// Orders sent on a tick are dispatched on the next one. That single tick of latency is what makes
// the loop well founded rather than re-entrant - an agent never sees the consequences of its own
// order inside the call that placed it - and it is what a participant experiences anyway.
//
// Own events reach an agent before the public messages for the same dispatch, which is the order
// a real participant sees them in: a fill is reported to the party to it before the print reaches
// the feed.
//
// The clock decides what kind of run this is, and nothing else changes. A ManualClock stepped by
// Run gives a run that reproduces exactly - the shape tests and recorded traces want. A
// SystemClock with Tick pumped on a timer gives a live venue that agents quote into and anything
// else can trade against through Submit.
//
// Single-threaded, like everything it composes.
public sealed class AgentVenue
{
    private readonly IClock _clock;
    private readonly InstrumentGroup _group;
    private readonly LiveDriver _driver;

    private readonly List<IAgent> _agents = new();
    private readonly Dictionary<string, List<IAgent>> _byCompany = new();
    private readonly Dictionary<string, List<IAgent>> _bySymbol = new();

    public AgentVenue(IClock clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _group = new InstrumentGroup(clock.GetCurrentTime());
        _driver = new LiveDriver(_group.Sequencer, clock);
    }

    public IClock Clock => _clock;

    public InstrumentGroup Group => _group;

    public MarketDataChannel Channel => _group.Channel;

    // In registration order, which is the order they are asked to act in.
    public IReadOnlyList<IAgent> Agents => _agents;

    public void Add(Instrument instrument, MarketSchedule schedule, int maxLevels = 10) =>
        _group.Add(instrument, schedule, maxLevels);

    // A pre-built book, for an instrument that needs restrictions wired onto it.
    public void Add(IOrderBook book, MarketSchedule schedule, int maxLevels = 10) =>
        _group.Add(book, schedule, maxLevels);

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

    // Flow from outside the agent population - a test's own orders, a gateway, whatever is
    // trading against them. Stamped by the driver exactly as an agent's own actions are, because
    // neither gets to say when it arrived.
    public void Submit(OrderBookAction action) => _driver.Submit(action);

    // Dispatches everything due as of the clock, routes what came back, and collects what the
    // agents want to send. Returns the channel's messages for this tick, so a host can publish
    // them onward - a subscriber outside the venue sees exactly what the agents saw.
    public IReadOnlyList<ChannelMessage> Tick()
    {
        // Read once, so every agent acting on this tick is answering the same question. The
        // driver reads the clock again for the dispatch and for each stamp; on a system clock
        // those readings can differ by a shade, and the sequencer only requires that they never
        // go backwards.
        var now = _clock.GetCurrentTime();

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
    // Deterministic given deterministic agents, which is what a seeded one is: the same seeds and
    // the same step produce the same run, message for message.
    public static void Run(AgentVenue venue, TimeSpan step, int ticks,
        Action<ChannelMessage>? onPublished = null)
    {
        ArgumentNullException.ThrowIfNull(venue);

        if (venue.Clock is not ManualClock clock)
            throw new ArgumentException(
                "a stepped run drives the clock itself, so the venue must hold a ManualClock. A venue " +
                "on a system clock is ticked by whoever owns that clock.", nameof(venue));

        if (step <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(step), step,
                "a step moves the clock forward; time runs one way");

        ArgumentOutOfRangeException.ThrowIfNegative(ticks);

        for (var i = 0; i < ticks; i++)
        {
            clock.SetCurrentTime(clock.GetCurrentTime() + step);

            foreach (var message in venue.Tick())
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
