using Circus.Actions;
using Circus.MarketData;

namespace Circus.Sequencing;

public static class Replay
{
    public static void Run(Sequencer sequencer, IEnumerable<OrderBookAction> trace,
        Action<Dispatched>? onDispatched = null, DateTime? until = null)
    {
        ArgumentNullException.ThrowIfNull(sequencer);
        ArgumentNullException.ThrowIfNull(trace);

        foreach (var action in trace)
        {
            sequencer.Submit(action);

            Report(sequencer.AdvanceTo(action.Time), onDispatched);
        }

        if (until is { } end && end > sequencer.LogicalNow)
            Report(sequencer.AdvanceTo(end), onDispatched);
    }

    public static IReadOnlyList<ChannelMessage> Run(InstrumentGroup group,
        IEnumerable<OrderBookAction> trace, DateTime? until = null)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(trace);

        var messages = new List<ChannelMessage>();
        Run(group.Sequencer, trace, d => messages.AddRange(group.Channel.Publish(d.Events)), until);
        return messages;
    }

    public static IReadOnlyDictionary<string, IReadOnlyList<ChannelMessage>> RunAll(
        InstrumentGroup group, IEnumerable<OrderBookAction> trace, DateTime? until = null)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(trace);

        var names = group.ChannelNames;
        var collected = names.ToDictionary(name => name, _ => new List<ChannelMessage>());

        Run(group.Sequencer, trace, dispatched =>
        {
            foreach (var name in names)
                collected[name].AddRange(group.ChannelNamed(name).Publish(dispatched.Events));
        }, until);

        return collected.ToDictionary(
            entry => entry.Key, entry => (IReadOnlyList<ChannelMessage>) entry.Value);
    }

    private static void Report(IReadOnlyList<Dispatched> dispatched, Action<Dispatched>? onDispatched)
    {
        if (onDispatched == null) return;

        foreach (var d in dispatched)
            onDispatched(d);
    }
}
