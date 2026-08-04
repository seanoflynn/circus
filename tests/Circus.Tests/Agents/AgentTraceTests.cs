using System.Security.Cryptography;
using System.Text;
using Circus.Actions;
using Circus.Agents;
using Circus.MarketData;
using Circus.Sequencing;
using Circus.Sessions;
using NUnit.Framework;

namespace Circus.Tests.Agents;

// A recording is what the rest of the suite and the benchmarks are fed on, so what is worth
// pinning is that a seed reproduces one exactly, that it is replayable at all - every action
// stamped, in order, naming orders that exist - and that replaying it into a fresh venue
// reproduces the run rather than merely resembling it.
[TestFixture]
public class AgentTraceTests
{
    private static readonly Instrument Gold = new("GCZ6", 10, 10);
    private static readonly Instrument Silver = new("SIZ6", 10, 10);
    private static readonly Instrument Copper = new("HGZ6", 10, 10);

    private static readonly DateTime Day = new(2000, 1, 1);

    private static MarketSchedule OpenThroughout() =>
        new(new TimeSpan(8, 0, 0), new TimeSpan(8, 30, 0), new TimeSpan(17, 0, 0));

    [Test]
    public void SameSeed_SameTrace()
    {
        var first = AgentTrace.Record(new[] {Gold, Silver}, 200, seed: 123);
        var second = AgentTrace.Record(new[] {Gold, Silver}, 200, seed: 123);

        Assert.That(Describe(first), Is.EqualTo(Describe(second)));
    }

    [Test]
    public void DifferentSeeds_DifferentTraces()
    {
        var first = AgentTrace.Record(new[] {Gold, Silver}, 200, seed: 1);
        var second = AgentTrace.Record(new[] {Gold, Silver}, 200, seed: 2);

        Assert.That(Describe(first), Is.Not.EqualTo(Describe(second)));
    }

    [TestCase(1)]
    [TestCase(7)]
    [TestCase(200)]
    [TestCase(1_001)]
    public void ReturnsExactlyWhatWasAskedFor(int actionCount)
    {
        // a tick writes as many actions as it likes, so all but the roundest counts land mid-tick
        // and are trimmed back
        Assert.That(AgentTrace.Record(new[] {Gold, Silver}, actionCount, seed: 5),
            Has.Count.EqualTo(actionCount));
    }

    [Test]
    public void NoActions_EmptyTrace()
    {
        Assert.That(AgentTrace.Record(Gold, 0, seed: 5), Is.Empty);
    }

    [Test]
    public void EveryActionIsStamped()
    {
        // a sequencer refuses an action with no time on it, so an unstamped recording would be a
        // trace that cannot be replayed
        foreach (var action in AgentTrace.Record(new[] {Gold, Silver}, 300, seed: 6))
            Assert.That(action.Time, Is.Not.EqualTo(default(DateTime)));
    }

    [Test]
    public void TimeNeverRunsBackwards()
    {
        var times = AgentTrace.Record(new[] {Gold, Silver}, 300, seed: 7).Select(a => a.Time).ToList();

        // one clock across every instrument, so a trace is already in the order a sequencer would
        // put it in. Equal stamps are expected rather than avoided: a tick writes everything it
        // writes at one instant, which is the tie the sequencer exists to settle.
        Assert.That(times, Is.EqualTo(times.OrderBy(t => t).ToList()));
        Assert.That(times.Distinct().Count(), Is.LessThan(times.Count));
    }

    [Test]
    public void SeveralInstruments_AreInterleavedInOneTrace()
    {
        var trace = AgentTrace.Record(new[] {Gold, Silver, Copper}, 600, seed: 8);

        var symbols = trace.Select(a => a.Symbol).ToList();
        Assert.That(symbols.Distinct().OrderBy(s => s),
            Is.EqualTo(new[] {Gold.Symbol, Silver.Symbol, Copper.Symbol}.OrderBy(s => s)));

        var switches = symbols.Zip(symbols.Skip(1)).Count(pair => pair.First != pair.Second);
        Assert.That(switches, Is.GreaterThan(10), "expected the instruments to be interleaved, not blocked");
    }

    [Test]
    public void EveryUpdateAndCancelNamesAnOrderTheSameCompanyCreatedInThatInstrument()
    {
        var trace = AgentTrace.Record(new[] {Gold, Silver}, 600, seed: 9);

        // an update or cancel naming an order that never existed would be routed to the book and
        // refused. A generator rolling dice needs a private copy of the book to avoid that; an
        // agent avoids it by having watched its own events.
        var known = new HashSet<(string Symbol, string CompanyId, string ClientOrderId)>();

        foreach (var action in trace)
        {
            switch (action)
            {
                case CreateOrder create:
                    known.Add((create.Symbol, create.CompanyId, create.ClientOrderId));
                    break;
                case UpdateOrder update:
                    Assert.That(known, Does.Contain((update.Symbol, update.CompanyId, update.PreviousClientOrderId)));
                    known.Add((update.Symbol, update.CompanyId, update.ClientOrderId));
                    break;
                case CancelOrder cancel:
                    Assert.That(known, Does.Contain((cancel.Symbol, cancel.CompanyId, cancel.PreviousClientOrderId)));
                    break;
            }
        }
    }

    [Test]
    public void ClientOrderIdsAreUniqueWithinACompany()
    {
        var trace = AgentTrace.Record(new[] {Gold, Silver, Copper}, 600, seed: 10);

        // a book keys orders by (CompanyId, ClientOrderId), so that is the pair that has to be
        // unique - two agents each numbering from one is fine, and is what lets an agent mint ids
        // without a counter shared across the venue
        var created = trace.OfType<CreateOrder>().Select(c => (c.CompanyId, c.ClientOrderId)).ToList();
        Assert.That(created.Distinct().Count(), Is.EqualTo(created.Count));
    }

    [Test]
    public void ItCarriesMoreThanOneCompany()
    {
        var trace = AgentTrace.Record(Gold, 400, seed: 11);

        // a recording carries the agents that wrote it, so a company means something across the
        // whole of it. A generator minting a fresh company per order would put self-match
        // prevention, inventory and anything else about a participant out of reach.
        Assert.That(trace.OfType<OrderAction>().Select(a => a.CompanyId).Distinct().Count(),
            Is.EqualTo(2));
    }

    [Test]
    public void ItCarriesTheWholeVocabulary()
    {
        var trace = AgentTrace.Record(new[] {Gold, Silver}, 2_000, seed: 12);

        // the defaults are chosen for coverage rather than realism, so a trace exercises the book
        // rather than one corner of it
        Assert.That(trace.OfType<CreateLimitOrder>(), Is.Not.Empty);
        Assert.That(trace.OfType<CreateMarketOrder>(), Is.Not.Empty);
        Assert.That(trace.OfType<UpdateOrder>(), Is.Not.Empty);
        Assert.That(trace.OfType<CancelOrder>(), Is.Not.Empty);
    }

    [Test]
    public void ReplayingARecording_ReproducesTheRun()
    {
        var trace = AgentTrace.Record(new[] {Gold, Silver}, 600, seed: 13);

        var first = Publish(trace);
        var second = Publish(trace);

        // market data is a function of the dispatch stream, which is a function of the trace - so
        // a feed can be rebuilt from a recording rather than having to have been recorded itself
        Assert.That(first, Is.EqualTo(second));
        Assert.That(first, Is.Not.Empty);

        // and the recording was rich enough to have traded
        Assert.That(first.Count(m => m.Contains(nameof(TradeDataEvent))), Is.GreaterThan(0));
    }

    [Test]
    public void AgentsThatNeverAct_SayWhyRatherThanSpinning()
    {
        var idle = new LiquidityAgentOptions(ActProbability: 0);

        var thrown = Assert.Throws<InvalidOperationException>(
            () => AgentTrace.Record(Gold, 10, seed: 14, options: idle));

        Assert.That(thrown.Message, Does.Contain("ActProbability"));
    }

    [Test]
    public void NoInstruments_Refused()
    {
        Assert.Throws<ArgumentException>(() => AgentTrace.Record(Array.Empty<Instrument>(), 10));
    }

    [Test]
    public void NoAgents_Refused()
    {
        Assert.Throws<ArgumentException>(() => AgentTrace.Record(Gold, 10, agents: 0));
    }

    // A committed fingerprint of one seeded recording. It asserts nothing about what good flow
    // looks like - only that this seed still produces the trace it produced when the value below
    // was taken, so a change to how agents behave has to be a deliberate one that updates this
    // rather than a quiet one nobody notices.
    //
    // Recompute and replace the constant when agent behaviour changes on purpose; the failure
    // message carries the new value.
    [Test]
    public void AFixedSeed_StillProducesTheTraceItAlwaysHas()
    {
        var trace = AgentTrace.Record(new[] {Gold, Silver}, 500, seed: 12345);
        var actual = Fingerprint(trace);

        Assert.That(actual, Is.EqualTo("8E13AAF0E66FBAC5E31DBE74B066BD80F879D226A6C1105D96853F905334BAE5"),
            $"the recording for this seed has changed. If that was deliberate, the new fingerprint is {actual}");
    }

    private static string Fingerprint(IReadOnlyList<OrderBookAction> trace)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", Describe(trace))));
        return Convert.ToHexString(bytes);
    }

    // Rendered rather than compared directly: an action is a record, and the ones carrying an
    // OrderValidity compare it by reference.
    private static List<string> Describe(IReadOnlyList<OrderBookAction> trace) =>
        trace.Select(a => FormattableString.Invariant($"{a.Symbol} {a.Time:O} {a}")).ToList();

    private static List<string> Publish(IReadOnlyList<OrderBookAction> trace)
    {
        var group = new InstrumentGroup(Day);
        group.Add(Gold, OpenThroughout());
        group.Add(Silver, OpenThroughout());

        return Replay.Run(group, trace)
            .Select(m => FormattableString.Invariant($"{m.Sequence} {m.Data.Symbol} {Render(m.Data)}"))
            .ToList();
    }

    private static string Render(MarketDataEvent data) => data switch
    {
        LevelsDataEvent levels =>
            $"{nameof(LevelsDataEvent)} [{string.Join(",", levels.Bids)}] [{string.Join(",", levels.Offers)}]",
        _ => data.ToString()
    };
}
