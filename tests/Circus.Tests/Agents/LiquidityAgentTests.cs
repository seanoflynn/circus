using Circus.Actions;
using Circus.Agents;
using Circus.Events;
using Circus.MarketData;
using Circus.Sequencing;
using Circus.Sessions;
using Circus.Time;
using NUnit.Framework;

namespace Circus.Tests.Agents;

// What is worth pinning about a seeded agent is that a seed reproduces a run exactly, that every
// parameter does what it says, and that the agent never sends the venue something the venue has to
// refuse - the last being the whole reason the shadow book existed.
[TestFixture]
public class LiquidityAgentTests
{
    private static readonly Instrument Gold = new("GCZ6", 10, 10);
    private static readonly Instrument Silver = new("SIZ6", 10, 10);

    private static readonly DateTime Day = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime BeforeTheDay = Day.AddHours(7);
    private static readonly DateTime Trading = Day.AddHours(9);

    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(1);

    private static MarketSchedule OpenThroughout() =>
        new(new TimeSpan(8, 0, 0), new TimeSpan(8, 30, 0), new TimeSpan(17, 0, 0));

    // Never opens within a run that starts at 09:00, so an agent watching it has nothing to
    // quote into.
    private static MarketSchedule Overnight() =>
        new(new TimeSpan(22, 0, 0), new TimeSpan(22, 30, 0), new TimeSpan(23, 30, 0));

    // The venue an agent trades at, wired the way a host wires one. The swarm builds none of it.
    private sealed class Venue
    {
        public Venue(MarketSchedule schedule, params Instrument[] instruments)
        {
            Clock = new ManualClock(BeforeTheDay);
            Group = new InstrumentGroup(Clock.GetCurrentTime());

            foreach (var instrument in instruments)
                Group.Add(instrument, schedule);

            Driver = new LiveDriver(Group.Sequencer, Clock);
            Swarm = new AgentSwarm(Group, Driver);
        }

        public Venue(params Instrument[] instruments) : this(OpenThroughout(), instruments)
        {
        }

        public ManualClock Clock { get; }
        public InstrumentGroup Group { get; }
        public LiveDriver Driver { get; }
        public AgentSwarm Swarm { get; }

        public List<ChannelMessage> Published { get; } = new();

        // Starts the run at a point where the schedule has already put the books into their
        // session, then steps a millisecond at a time.
        public void Run(int ticks)
        {
            if (Clock.GetCurrentTime() < Trading)
                Clock.SetCurrentTime(Trading);

            AgentSwarm.Run(Swarm, Clock, Step, ticks, Published.Add);
        }

        public LevelsDataEvent LastLevels(string symbol) =>
            Published.Select(m => m.Data).OfType<LevelsDataEvent>().LastOrDefault(l => l.Symbol == symbol);

        public int Trades => Published.Select(m => m.Data).OfType<TradeDataEvent>().Count();
    }

    // Wraps an agent to keep what the venue sent it and what it sent back. Kept here rather than
    // on LiquidityAgent, which has no reason to carry a log of its own life around in production.
    private sealed class Recording : IAgent
    {
        private readonly IAgent _inner;
        private readonly List<OrderBookEvent> _ownEvents = new();
        private readonly List<OrderBookAction> _sent = new();

        public Recording(IAgent inner)
        {
            _inner = inner;
        }

        public string CompanyId => _inner.CompanyId;
        public IReadOnlyList<string> Symbols => _inner.Symbols;

        public IReadOnlyList<OrderBookEvent> OwnEvents => _ownEvents;
        public IReadOnlyList<OrderBookAction> Sent => _sent;

        public void OnMarketData(MarketDataEvent data) => _inner.OnMarketData(data);

        public void OnOwnEvent(OrderBookEvent ev)
        {
            _ownEvents.Add(ev);
            _inner.OnOwnEvent(ev);
        }

        public IReadOnlyList<OrderBookAction> Act(DateTime now)
        {
            var actions = _inner.Act(now);
            _sent.AddRange(actions);
            return actions;
        }
    }

    private static LiquidityAgent Agent(string companyId, LiquidityAgentOptions options, int seed,
        params Instrument[] instruments) =>
        new(companyId, instruments, options, seed);

    [Test]
    public void SameSeed_SameRun()
    {
        Assert.That(Render(7), Is.EqualTo(Render(7)));
        Assert.That(Render(7), Is.Not.Empty);
    }

    [Test]
    public void DifferentSeeds_DifferentRuns()
    {
        Assert.That(Render(1), Is.Not.EqualTo(Render(2)));
    }

    private static List<string> Render(int seed)
    {
        var venue = new Venue(Gold, Silver);
        venue.Swarm.Add(Agent("MM", new LiquidityAgentOptions(), seed, Gold, Silver));
        venue.Run(200);

        return venue.Published
            .Select(m => $"{m.Sequence} {m.Data.Symbol} {Describe(m.Data)}")
            .ToList();
    }

    [Test]
    public void Quotes_BothSidesOfWhereItThinksTheMarketIs()
    {
        var venue = new Venue(Gold);
        venue.Swarm.Add(Agent("MM", new LiquidityAgentOptions(Aggression: 0), 11, Gold));
        venue.Run(100);

        var levels = venue.LastLevels(Gold.Symbol);
        Assert.That(levels, Is.Not.Null);
        Assert.That(levels.Bids, Is.Not.Empty);
        Assert.That(levels.Offers, Is.Not.Empty);

        // rung 0 sits one spacing off the reference rather than on it, so the agent's own bid and
        // offer are always a rung apart and it never quotes itself into a trade
        Assert.That(levels.Bids[0].Price, Is.LessThan(levels.Offers[0].Price));

        // and it quoted around what it was told to assume, the book having said nothing first
        Assert.That(levels.Bids[0].Price, Is.LessThan(1000));
        Assert.That(levels.Offers[0].Price, Is.GreaterThan(1000));
    }

    [Test]
    public void ALoneAgent_IsNeverRefusedAnything()
    {
        // No other participant, so nothing can race it: every action it sends is one the venue
        // should accept. A generator keeping a private copy of the book would have this by
        // construction; an agent has it only if it read its own events properly, so it is
        // checked against the real venue rather than assumed.
        var venue = new Venue(Gold);
        var agent = new Recording(Agent("MM", new LiquidityAgentOptions(), 3, Gold));
        venue.Swarm.Add(agent);

        venue.Run(400);

        var rejected = agent.OwnEvents.OfType<OrderRejectedEvent>().ToList();
        Assert.That(rejected, Is.Empty,
            $"refused: {string.Join(", ", rejected.Select(r => $"{r.GetType().Name}/{r.Reason}"))}");

        // and it did enough for that to mean something
        Assert.That(agent.Sent, Is.Not.Empty);
    }

    [Test]
    public void CancelsAndUpdates_OnlyEverNameOrdersItCreated()
    {
        // Two agents crossing each other, so orders do get filled out from under one another -
        // the case where a participant tracking its own state badly would start naming orders
        // that are no longer there.
        var venue = new Venue(Gold);

        var first = new Recording(Agent("MM1", new LiquidityAgentOptions(Aggression: 0.3), 5, Gold));
        var second = new Recording(Agent("MM2", new LiquidityAgentOptions(Aggression: 0.3), 6, Gold));

        venue.Swarm.Add(first);
        venue.Swarm.Add(second);
        venue.Run(400);

        foreach (var agent in new[] {first, second})
        {
            var known = new HashSet<string>();

            foreach (var action in agent.Sent)
            {
                switch (action)
                {
                    case CreateOrder create:
                        known.Add(create.ClientOrderId);
                        break;
                    case UpdateOrder update:
                        Assert.That(known, Does.Contain(update.PreviousClientOrderId));
                        known.Add(update.ClientOrderId);
                        break;
                    case CancelOrder cancel:
                        Assert.That(known, Does.Contain(cancel.PreviousClientOrderId));
                        break;
                }
            }
        }

        Assert.That(venue.Trades, Is.GreaterThan(0), "expected the two of them to trade");
    }

    [Test]
    public void Aggression_Zero_QuotesWithoutEverTrading()
    {
        var venue = new Venue(Gold);
        venue.Swarm.Add(Agent("MM1", new LiquidityAgentOptions(Aggression: 0), 8, Gold));
        venue.Swarm.Add(Agent("MM2", new LiquidityAgentOptions(Aggression: 0), 9, Gold));
        venue.Run(300);

        Assert.That(venue.LastLevels(Gold.Symbol).Bids, Is.Not.Empty, "expected it to be quoting");
        Assert.That(venue.Trades, Is.EqualTo(0));
    }

    [Test]
    public void Aggression_Crosses()
    {
        var venue = new Venue(Gold);
        venue.Swarm.Add(Agent("MM1", new LiquidityAgentOptions(Aggression: 0.5), 8, Gold));
        venue.Swarm.Add(Agent("MM2", new LiquidityAgentOptions(Aggression: 0.5), 9, Gold));
        venue.Run(300);

        Assert.That(venue.Trades, Is.GreaterThan(0));
    }

    [Test]
    public void Depth_DecidesHowManyLevelsItQuotes()
    {
        Assert.That(BidLevels(depth: 1), Is.EqualTo(1));
        Assert.That(BidLevels(depth: 4), Is.GreaterThan(1));

        static int BidLevels(int depth)
        {
            var venue = new Venue(Gold);
            venue.Swarm.Add(Agent("MM",
                new LiquidityAgentOptions(Depth: depth, Aggression: 0, CancelProbability: 0,
                    ReplaceProbability: 0), 4, Gold));
            venue.Run(100);

            return venue.LastLevels(Gold.Symbol).Bids.Count;
        }
    }

    [Test]
    public void LevelSpacing_WidensTheLadder()
    {
        var venue = new Venue(Gold);
        venue.Swarm.Add(Agent("MM",
            new LiquidityAgentOptions(Depth: 2, LevelSpacingTicks: 3, Aggression: 0, CancelProbability: 0,
                ReplaceProbability: 0), 12, Gold));
        venue.Run(100);

        var levels = venue.LastLevels(Gold.Symbol);

        // three ticks of 10 between the reference and the first rung, and between rungs
        Assert.That(levels.Bids[0].Price, Is.EqualTo(970));
        Assert.That(levels.Offers[0].Price, Is.EqualTo(1030));
    }

    [Test]
    public void MaxLiveOrders_IsNeverExceeded()
    {
        var venue = new Venue(Gold, Silver);
        var agent = Agent("MM", new LiquidityAgentOptions(Depth: 8, MaxLiveOrders: 5), 13, Gold, Silver);
        venue.Swarm.Add(agent);

        venue.Clock.SetCurrentTime(Trading);

        // checked every tick rather than at the end, since a limit only breached in the middle
        // is still breached
        for (var i = 0; i < 200; i++)
        {
            AgentSwarm.Run(venue.Swarm, venue.Clock, Step, 1);
            Assert.That(agent.Orders.LiveCount, Is.LessThanOrEqualTo(5));
        }

        Assert.That(agent.Orders.LiveCount, Is.GreaterThan(0), "expected it to have quoted something");
    }

    [Test]
    public void ActProbability_Zero_SendsNothing()
    {
        var venue = new Venue(Gold);
        var agent = new Recording(Agent("MM", new LiquidityAgentOptions(ActProbability: 0), 1, Gold));
        venue.Swarm.Add(agent);

        venue.Run(200);

        Assert.That(agent.Sent, Is.Empty);
    }

    [Test]
    public void AClosedBook_IsNotQuotedInto()
    {
        var venue = new Venue(Overnight(), Gold);
        var agent = new Recording(Agent("MM", new LiquidityAgentOptions(), 1, Gold));
        venue.Swarm.Add(agent);

        venue.Run(200);

        // the schedule never opens within the run, and an agent that quoted anyway would have
        // every order refused
        Assert.That(agent.Sent, Is.Empty);
    }

    [Test]
    public void MaxPosition_Zero_LeavesNoSideItIsWillingToAddTo()
    {
        var venue = new Venue(Gold);
        var agent = new Recording(Agent("MM", new LiquidityAgentOptions(MaxPosition: 0), 1, Gold));
        venue.Swarm.Add(agent);

        venue.Run(200);

        // flat, and neither side would move it back towards flat
        Assert.That(agent.Sent, Is.Empty);
    }

    [Test]
    public void MaxVisibleQuantity_ShowsOnlyThePeak()
    {
        var venue = new Venue(Gold);
        var agent = Agent("MM",
            new LiquidityAgentOptions(MinQuantity: 5, MaxQuantity: 10, MaxVisibleQuantity: 2, Aggression: 0),
            14, Gold);
        venue.Swarm.Add(agent);

        venue.Run(100);

        Assert.That(agent.Orders.LiveOrders, Is.Not.Empty);
        foreach (var order in agent.Orders.LiveOrders)
            Assert.That(order.DisplayedQuantity, Is.LessThanOrEqualTo(2));
    }

    [Test]
    public void SeveralInstruments_AreAllQuoted()
    {
        var venue = new Venue(Gold, Silver);
        venue.Swarm.Add(Agent("MM", new LiquidityAgentOptions(Aggression: 0), 15, Gold, Silver));
        venue.Run(200);

        Assert.That(venue.LastLevels(Gold.Symbol).Bids, Is.Not.Empty);
        Assert.That(venue.LastLevels(Silver.Symbol).Bids, Is.Not.Empty);
    }

    [Test]
    public void ASeedIsReportedWhetherGivenOrDrawn()
    {
        Assert.That(new LiquidityAgent("MM", new[] {Gold}, seed: 42).Seed, Is.EqualTo(42));

        // drawn rather than given, and still reported, so a fuzzing run that finds something can
        // be replayed
        var drawn = new LiquidityAgent("MM", new[] {Gold});
        Assert.That(new LiquidityAgent("MM", new[] {Gold}, seed: drawn.Seed).Seed, Is.EqualTo(drawn.Seed));
    }

    [Test]
    public void NoInstruments_Refused()
    {
        Assert.Throws<ArgumentException>(() => new LiquidityAgent("MM", Array.Empty<Instrument>()));
    }

    [Test]
    public void ALongClientOrderIdPrefix_Refused()
    {
        // the book allows 20 characters for a client order id, and the counter needs the rest
        Assert.Throws<ArgumentException>(
            () => new LiquidityAgent("a-very-long-company-id", new[] {Gold}));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void ADepthBelowOne_Refused(int depth)
    {
        Assert.Throws<ArgumentException>(() => new LiquidityAgentOptions(Depth: depth).Validate());
    }

    [TestCase(-0.1)]
    [TestCase(1.1)]
    public void AProbabilityOutsideZeroToOne_Refused(double probability)
    {
        Assert.Throws<ArgumentException>(
            () => new LiquidityAgentOptions(Aggression: probability).Validate());
    }

    [Test]
    public void AMaxQuantityBelowMinQuantity_Refused()
    {
        Assert.Throws<ArgumentException>(
            () => new LiquidityAgentOptions(MinQuantity: 5, MaxQuantity: 4).Validate());
    }

    // Rendered rather than compared directly: LevelsDataEvent holds its ladders in lists, and a
    // record's generated equality compares those by reference.
    private static string Describe(MarketDataEvent data) => data switch
    {
        LevelsDataEvent levels =>
            $"Levels {levels.Time:O} [{string.Join(",", levels.Bids)}] [{string.Join(",", levels.Offers)}]",
        _ => data.ToString()
    };
}
