using Circus.Actions;
using Circus.Simulator;
using NUnit.Framework;

namespace Circus.Tests.Simulator;

// The simulator generates traces the rest of the suite and the benchmarks depend on, so what is
// worth pinning is that a seed reproduces one exactly, and that covering several securities
// interleaves them without letting one security's flow depend on another's.
[TestFixture]
public class OrderFlowSimulatorTests
{
    private static readonly Security Gold = new("GCZ6", SecurityType.Future, 10, 10);
    private static readonly Security Silver = new("SIZ6", SecurityType.Future, 10, 10);
    private static readonly Security Copper = new("HGZ6", SecurityType.Future, 10, 10);

    [Test]
    public void Generate_SameSeed_SameTrace()
    {
        var first = new OrderFlowSimulator(new[] {Gold, Silver}, seed: 123).Generate(200);
        var second = new OrderFlowSimulator(new[] {Gold, Silver}, seed: 123).Generate(200);

        Assert.AreEqual(Describe(first), Describe(second));
    }

    [Test]
    public void Generate_DifferentSeeds_DifferentTraces()
    {
        var first = new OrderFlowSimulator(new[] {Gold, Silver}, seed: 1).Generate(200);
        var second = new OrderFlowSimulator(new[] {Gold, Silver}, seed: 2).Generate(200);

        Assert.AreNotEqual(Describe(first), Describe(second));
    }

    [Test]
    public void Generate_SeveralSecurities_InterleavesThemInOneTrace()
    {
        var trace = new OrderFlowSimulator(new[] {Gold, Silver, Copper}, seed: 7).Generate(600);

        // every security represented, and mixed rather than generated in blocks
        var names = trace.Select(a => a.Security.Name).ToList();
        Assert.That(
            names.Distinct().OrderBy(n => n).ToList(),
            Is.EqualTo(new[] {Gold.Name, Copper.Name, Silver.Name}.OrderBy(n => n).ToList()));

        var switches = names.Zip(names.Skip(1)).Count(pair => pair.First != pair.Second);
        Assert.Greater(switches, 100, "expected the securities to be interleaved, not blocked");
    }

    [Test]
    public void Generate_TimeRunsForwardAcrossTheWholeTrace()
    {
        var trace = new OrderFlowSimulator(new[] {Gold, Silver}, seed: 5).Generate(300);

        // one clock across all securities, so the trace is already in the order a sequencer
        // would put it in
        var times = trace.Select(a => a.Time).ToList();
        Assert.AreEqual(times.OrderBy(t => t).ToList(), times);
        Assert.AreEqual(times.Count, times.Distinct().Count(), "each action gets its own instant");
    }

    [Test]
    public void Generate_EveryActionTargetsTheSecurityItWasGeneratedFor()
    {
        var trace = new OrderFlowSimulator(new[] {Gold, Silver}, seed: 8).Generate(400);

        // an update or cancel naming an order that never existed in that book would be routed to
        // it and rejected, so the ids have to have been drawn per security
        var known = new HashSet<(string Security, string ClientOrderId)>();

        foreach (var action in trace)
        {
            var name = action.Security.Name;

            switch (action)
            {
                case CreateOrder create:
                    known.Add((name, create.ClientOrderId));
                    break;
                case UpdateOrder update:
                    Assert.IsTrue(known.Contains((name, update.PreviousClientOrderId)),
                        $"{name} updated {update.PreviousClientOrderId}, which it never created");
                    known.Add((name, update.ClientOrderId));
                    break;
                case CancelOrder cancel:
                    Assert.IsTrue(known.Contains((name, cancel.PreviousClientOrderId)),
                        $"{name} cancelled {cancel.PreviousClientOrderId}, which it never created");
                    break;
            }
        }
    }

    [Test]
    public void Generate_ClientOrderIdsAreUniqueAcrossSecurities()
    {
        var trace = new OrderFlowSimulator(new[] {Gold, Silver, Copper}, seed: 3).Generate(400);

        // one counter across all books, so nothing in a venue-wide trace collides even though
        // each book would only have noticed a collision within itself
        var created = trace.OfType<CreateOrder>().Select(c => c.ClientOrderId).ToList();
        Assert.AreEqual(created.Count, created.Distinct().Count());
    }

    [Test]
    public void Generate_NoSecurities_ArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new OrderFlowSimulator(Array.Empty<Security>()));
    }

    private static List<string> Describe(IReadOnlyList<OrderBookAction> trace) =>
        trace.Select(a => $"{a.Security.Name} {a.Time:O} {a}").ToList();
}
