using Circus.Actions;
using Circus.Agents;
using Circus.MarketData;
using Circus.Sequencing;
using Circus.Sessions;
using Circus.Time;

namespace Circus.Examples;

// A venue with participants in it, and somebody trading against them.
//
// The wiring is LiveVenueExample's, unchanged: a group holding the book and its schedule, a
// driver stamping arriving actions from a clock. The swarm attaches to both rather than building
// either, and adds the one thing a venue on its own has no way to do - hand each agent what it is
// entitled to see, so that it can decide what to send next.
//
// An agent knows the market because it subscribed to the feed and knows what it is holding
// because it saw its own confirms and fills. There is no book anywhere in here modelling the
// venue's book: the agents quote off the same depth messages any other subscriber receives.
//
// The order that trades through them at the end is not an agent and the swarm is never told about
// it. It goes in through the same driver a gateway would use, which is the whole point - what the
// agents provide is a market you can send an order to and get filled by.
//
// Deterministic, like every sample here: a ManualClock, and agents whose every decision comes
// from a seed. Run it twice and it prints the same thing twice.
public static class AgentSwarmExample
{
    private static readonly Instrument Gold = new("GCZ6", TickSize: 1);

    private static readonly DateTime Day = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

    private static readonly MarketSchedule Schedule =
        new(new TimeSpan(8, 30, 0), new TimeSpan(9, 0, 0), new TimeSpan(17, 0, 0));

    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(1);

    // Aggression is the dial that would have them cross the spread as well as quote it; at zero
    // they only ever add, which is what makes the single trade at the end unambiguously the one
    // sent from outside.
    private static readonly LiquidityAgentOptions Quoting = new(
        ReferencePrice: 1000m, Depth: 4, MinQuantity: 1, MaxQuantity: 5, Aggression: 0);

    public static void Run()
    {
        var clock = new ManualClock(Day.AddHours(8));

        var group = new InstrumentGroup(clock.GetCurrentTime());
        group.Add(Gold, Schedule);

        var driver = new LiveDriver(group.Sequencer, clock);
        var swarm = new AgentSwarm(group, driver);

        // Two firms rather than one. A lone agent is stopped from trading with itself by the
        // self-match prevention it carries, which is correct and makes for a quiet venue.
        var makers = new[]
        {
            new LiquidityAgent("MM1", new[] {Gold}, Quoting, seed: 1),
            new LiquidityAgent("MM2", new[] {Gold}, Quoting, seed: 2)
        };

        foreach (var maker in makers)
            swarm.Add(maker);

        // The schedule opens the book, not anything here. Both boundaries came due while nothing
        // was happening, and one tick dispatches them - and tells the agents, which is the moment
        // they start quoting.
        clock.SetCurrentTime(Day.AddHours(9));
        Display.Print(swarm.Tick(clock.GetCurrentTime()));

        // Fifty milliseconds of quoting. Nothing is printed for it: a subscriber would see
        // hundreds of messages, and what matters here is the book they add up to.
        var messages = 0;
        LevelsDataEvent? depth = null;

        AgentSwarm.Run(swarm, clock, Step, 50, message =>
        {
            messages++;
            if (message.Data is LevelsDataEvent levels) depth = levels;
        });

        Console.WriteLine($"  {messages} messages later, two agents are quoting:");
        Console.WriteLine($"    bids   {Ladder(depth?.Bids)}");
        Console.WriteLine($"    offers {Ladder(depth?.Offers)}");

        if (depth is not {Bids.Count: > 0}) return;

        // Priced through several of their bids and sized to clear them, so this sweeps the ladder
        // rather than trading a single level. Submitted straight to the driver: it carries no
        // time, because a participant does not get to say when its order reached the exchange.
        var sweep = depth.Bids.Take(3).ToList();

        Console.WriteLine($"  selling {sweep.Sum(l => l.Quantity)} down to {sweep[^1].Price}, "
                          + "from outside the swarm entirely:");

        driver.Submit(new CreateLimitOrder
        {
            Symbol = Gold.Symbol, CompanyId = "Taker", ClientOrderId = "T1",
            OrderValidity = new OrderValidity.Day(), Side = Side.Sell,
            Quantity = sweep.Sum(l => l.Quantity), Price = sweep[^1].Price
        });

        Display.Print(swarm.Tick(clock.GetCurrentTime()));

        // What each of them was left holding, from its own fills rather than from any question
        // asked of the book.
        foreach (var maker in makers)
            Console.WriteLine($"  {maker.CompanyId} bought {maker.Orders.Position(Gold.Symbol)}, "
                              + $"{maker.Orders.LiveCount} orders still resting");
    }

    private static string Ladder(IReadOnlyList<Level>? levels) =>
        levels is null or {Count: 0}
            ? "(none)"
            : string.Join("  ", levels.Take(4).Select(l => $"{l.Quantity}@{l.Price}"));
}
