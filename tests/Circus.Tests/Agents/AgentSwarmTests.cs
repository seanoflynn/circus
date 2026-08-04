using Circus.Actions;
using Circus.Agents;
using Circus.Events;
using Circus.MarketData;
using Circus.Sequencing;
using Circus.Sessions;
using Circus.Time;
using NUnit.Framework;

namespace Circus.Tests.Agents;

// The swarm, tested with scripted agents rather than deciding ones: what is worth pinning here is
// the wiring - that events reached the participant they belong to, that actions left in the order
// the swarm was told to ask in, and that the loop between the two is well founded.
[TestFixture]
public class AgentSwarmTests
{
    private static readonly Instrument Gold = new("GCZ6", 10, 10);
    private static readonly Instrument Silver = new("SIZ6", 10, 10);

    private static readonly DateTime Day = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // The venue starts before the day does, so the schedule's own boundaries put the books into
    // the session rather than anything here submitting them. A book registered mid-session is
    // never caught up by the sequencer, which is exactly the mistake this avoids.
    private static readonly DateTime BeforeTheDay = Day.AddHours(7);
    private static readonly DateTime Trading = Day.AddHours(9);

    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(1);

    private static MarketSchedule OpenThroughout() =>
        new(new TimeSpan(8, 0, 0), new TimeSpan(8, 30, 0), new TimeSpan(17, 0, 0));

    private static CreateLimitOrder Limit(Instrument instrument, string companyId, string clientOrderId,
        Side side, int quantity, decimal price) =>
        new()
        {
            Symbol = instrument.Symbol, CompanyId = companyId, ClientOrderId = clientOrderId,
            OrderValidity = new OrderValidity.Day(), Side = side, Quantity = quantity, Price = price
        };

    // A venue wired the way a host wires one - a group holding the books and their schedules, a
    // driver pumping its sequencer off a clock - with a swarm attached to both. The swarm builds
    // none of it, which is the point: the same group could be carrying restrictions, other
    // instruments, and flow from gateways that are not agents at all.
    private sealed class Venue
    {
        public Venue(params Instrument[] instruments)
        {
            Clock = new ManualClock(BeforeTheDay);
            Group = new InstrumentGroup(Clock.GetCurrentTime());

            foreach (var instrument in instruments)
                Group.Add(instrument, OpenThroughout());

            Driver = new LiveDriver(Group.Sequencer, Clock);
            Swarm = new AgentSwarm(Group, Driver);
        }

        public ManualClock Clock { get; }
        public InstrumentGroup Group { get; }
        public LiveDriver Driver { get; }
        public AgentSwarm Swarm { get; }

        // What a host does on a timer: move the clock, then tick.
        public IReadOnlyList<ChannelMessage> TickAt(DateTime time)
        {
            Clock.SetCurrentTime(time);
            return Swarm.Tick(time);
        }
    }

    [Test]
    public void AgentOrders_ReachTheBookOnTheFollowingTick()
    {
        var venue = new Venue(Gold);

        var agent = new ScriptedAgent("Buyer", Gold.Symbol);
        agent.Enqueue(Limit(Gold, "Buyer", "B1", Side.Buy, 3, 100));
        venue.Swarm.Add(agent);

        // the tick that opens the book is also the one the agent acts on, so nothing of its own
        // has been dispatched yet
        venue.TickAt(Trading);
        Assert.That(agent.Market.Of(Gold.Symbol).IsOpen, Is.True);
        Assert.That(agent.Orders.LiveCount, Is.EqualTo(0));

        // one tick of latency, which is what makes the loop well founded rather than re-entrant
        venue.TickAt(Trading + Step);
        Assert.That(agent.Orders.LiveCount, Is.EqualTo(1));
        Assert.That(agent.Orders["B1"].Price, Is.EqualTo(100));
        Assert.That(agent.Market.Of(Gold.Symbol).BestBid, Is.EqualTo(100));
    }

    [Test]
    public void OwnEvents_ReachOnlyTheCompanyTheyBelongTo()
    {
        var venue = new Venue(Gold);

        var buyer = new ScriptedAgent("Buyer", Gold.Symbol);
        buyer.Enqueue(Limit(Gold, "Buyer", "B1", Side.Buy, 3, 100));

        var seller = new ScriptedAgent("Seller", Gold.Symbol);
        seller.Enqueue(Limit(Gold, "Seller", "S1", Side.Sell, 3, 100));

        venue.Swarm.Add(buyer);
        venue.Swarm.Add(seller);

        venue.TickAt(Trading);
        venue.TickAt(Trading + Step);

        // neither has been handed anything belonging to the other, which is what a participant
        // feed is: a filter over the event stream by company
        Assert.That(buyer.OwnEvents, Is.Not.Empty);
        Assert.That(buyer.OwnEvents.OfType<OrderEvent>().Select(e => e.CompanyId).Distinct(),
            Is.EqualTo(new[] {"Buyer"}));
        Assert.That(seller.OwnEvents.OfType<OrderEvent>().Select(e => e.CompanyId).Distinct(),
            Is.EqualTo(new[] {"Seller"}));

        // and each knows its own side of the trade
        Assert.That(buyer.Orders.Position(Gold.Symbol), Is.EqualTo(3));
        Assert.That(seller.Orders.Position(Gold.Symbol), Is.EqualTo(-3));
        Assert.That(buyer.Orders.LiveCount, Is.EqualTo(0));
        Assert.That(seller.Orders.LiveCount, Is.EqualTo(0));
    }

    [Test]
    public void MarketData_ReachesOnlyTheInstrumentsAnAgentSubscribedTo()
    {
        var venue = new Venue(Gold, Silver);

        var agent = new ScriptedAgent("Buyer", Gold.Symbol);
        venue.Swarm.Add(agent);

        venue.TickAt(Trading);

        // flow in the instrument it does not follow, submitted straight to the driver the way a
        // gateway would
        venue.Driver.Submit(Limit(Silver, "Other", "O1", Side.Buy, 3, 100));
        venue.TickAt(Trading + Step);

        Assert.That(agent.MarketData, Is.Not.Empty);
        Assert.That(agent.MarketData.Select(m => m.Symbol).Distinct(), Is.EqualTo(new[] {Gold.Symbol}));
        Assert.That(agent.Market.Of(Silver.Symbol).Status, Is.EqualTo(OrderBookStatus.Closed));
    }

    [Test]
    public void ExternalFlow_TradesAgainstTheAgents()
    {
        var venue = new Venue(Gold);

        var agent = new ScriptedAgent("Maker", Gold.Symbol);
        agent.Enqueue(Limit(Gold, "Maker", "M1", Side.Buy, 3, 100));
        venue.Swarm.Add(agent);

        venue.TickAt(Trading);
        venue.TickAt(Trading + Step);
        Assert.That(agent.Orders.LiveCount, Is.EqualTo(1));

        // somebody outside the swarm entirely, trading into what the agents are quoting - the
        // same driver, stamped the same way, which is why the swarm need not know it happened
        venue.Driver.Submit(Limit(Gold, "Human", "H1", Side.Sell, 2, 100));

        venue.TickAt(Trading + Step + Step);

        Assert.That(agent.Orders.Position(Gold.Symbol), Is.EqualTo(2));
        Assert.That(agent.Orders["M1"].RemainingQuantity, Is.EqualTo(1));
        Assert.That(agent.Market.Of(Gold.Symbol).LastTradePrice, Is.EqualTo(100));
    }

    [Test]
    public void Rejections_ReachTheAgentThatCausedThem()
    {
        var venue = new Venue(Gold);

        // 105 is not a multiple of the instrument's tick
        var agent = new ScriptedAgent("Buyer", Gold.Symbol);
        agent.Enqueue(Limit(Gold, "Buyer", "B1", Side.Buy, 3, 105));
        venue.Swarm.Add(agent);

        venue.TickAt(Trading);
        venue.TickAt(Trading + Step);

        // an agent told nothing about a refusal would go on believing in an order that does not
        // exist, so the refusal is part of what it is owed
        var rejected = agent.OwnEvents.OfType<CreateOrderRejected>().ToList();
        Assert.That(rejected, Has.Count.EqualTo(1));
        Assert.That(rejected[0].Reason, Is.EqualTo(OrderRejectedReason.InvalidPriceIncrement));
        Assert.That(agent.Orders.LiveCount, Is.EqualTo(0));
    }

    [Test]
    public void AgentsAreAskedInRegistrationOrder()
    {
        var venue = new Venue(Gold);

        // the same bid from both, on the same tick, so the only thing separating them in the
        // queue is the order they were asked in
        var first = new ScriptedAgent("First", Gold.Symbol);
        first.Enqueue(Limit(Gold, "First", "F1", Side.Buy, 3, 100));

        var second = new ScriptedAgent("Second", Gold.Symbol);
        second.Enqueue(Limit(Gold, "Second", "S1", Side.Buy, 3, 100));

        venue.Swarm.Add(first);
        venue.Swarm.Add(second);

        venue.TickAt(Trading);
        venue.TickAt(Trading + Step);

        venue.Driver.Submit(Limit(Gold, "Human", "H1", Side.Sell, 3, 100));
        venue.TickAt(Trading + Step + Step);

        // price-time, and the first agent asked was the first in the book
        Assert.That(first.Orders.Position(Gold.Symbol), Is.EqualTo(3));
        Assert.That(second.Orders.Position(Gold.Symbol), Is.EqualTo(0));
        Assert.That(second.Orders.LiveCount, Is.EqualTo(1));
    }

    [Test]
    public void AgentsSharingACompany_BothSeeTheFirmsEvents()
    {
        var venue = new Venue(Gold);

        var desk = new ScriptedAgent("Firm", Gold.Symbol);
        desk.Enqueue(Limit(Gold, "Firm", "D1", Side.Buy, 3, 100));

        var backOffice = new ScriptedAgent("Firm", Gold.Symbol);

        venue.Swarm.Add(desk);
        venue.Swarm.Add(backOffice);

        venue.TickAt(Trading);
        venue.TickAt(Trading + Step);

        // one company, so both are handed the firm's events - the shape a drop copy has, and
        // what makes self-match prevention reachable at all
        Assert.That(desk.OwnEvents, Is.Not.Empty);
        Assert.That(backOffice.OwnEvents.Count, Is.EqualTo(desk.OwnEvents.Count));
        Assert.That(backOffice.Orders.LiveCount, Is.EqualTo(1));
    }

    [Test]
    public void Run_StepsTheClockAndTicks()
    {
        var venue = new Venue(Gold);

        var agent = new ScriptedAgent("Buyer", Gold.Symbol);
        venue.Swarm.Add(agent);

        venue.Clock.SetCurrentTime(Trading);

        var published = new List<ChannelMessage>();
        AgentSwarm.Run(venue.Swarm, venue.Clock, Step, 5, published.Add);

        Assert.That(venue.Clock.GetCurrentTime(), Is.EqualTo(Trading + TimeSpan.FromMilliseconds(5)));

        // the schedule's own boundaries came due within the run, so the agent was told the book
        // opened without anything here submitting it
        Assert.That(published, Is.Not.Empty);
        Assert.That(agent.Market.Of(Gold.Symbol).IsOpen, Is.True);

        // one contiguous run of numbers, the same promise the channel makes to any subscriber
        Assert.That(published.Select(m => m.Sequence),
            Is.EqualTo(Enumerable.Range(1, published.Count).Select(i => (long) i)));
    }

    [Test]
    public void Run_ANonAdvancingStep_Refused()
    {
        var venue = new Venue(Gold);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => AgentSwarm.Run(venue.Swarm, venue.Clock, TimeSpan.Zero, 5));
    }

    [Test]
    public void TheSameRunTwice_PublishesTheSameMessages()
    {
        // the swarm itself must add no nondeterminism of its own - no dictionary iteration
        // deciding who is asked first, no ordering that depends on how routing was indexed
        Assert.That(Render(), Is.EqualTo(Render()));
        Assert.That(Render(), Is.Not.Empty);

        static List<string> Render()
        {
            var venue = new Venue(Gold, Silver);

            var buyer = new ScriptedAgent("Buyer", Gold.Symbol, Silver.Symbol);
            buyer.Enqueue(Limit(Gold, "Buyer", "B1", Side.Buy, 3, 100));
            buyer.Enqueue(Limit(Silver, "Buyer", "B2", Side.Buy, 2, 90));

            var seller = new ScriptedAgent("Seller", Gold.Symbol);
            seller.Enqueue(Limit(Gold, "Seller", "S1", Side.Sell, 5, 110));
            seller.Enqueue(Limit(Gold, "Seller", "S2", Side.Sell, 5, 100));

            venue.Swarm.Add(buyer);
            venue.Swarm.Add(seller);

            venue.Clock.SetCurrentTime(Trading);

            var rendered = new List<string>();
            AgentSwarm.Run(venue.Swarm, venue.Clock, Step, 10,
                m => rendered.Add($"{m.Sequence} {m.Data.Symbol} {Describe(m.Data)}"));

            return rendered;
        }
    }

    // Every published message carries only scalars now that depth arrives a level at a time, so a
    // record's own ToString is faithful and its equality is by value.
    private static string Describe(MarketDataEvent data) => data.ToString()!;
}
