using Circus.Actions;
using Circus.MarketData;

namespace Circus.Sequencing;

// Feeds a recorded trace through a sequencer, with no clock anywhere: every instant involved
// comes from the actions themselves. That is the whole difference between this and LiveDriver,
// and it is what makes a replay reproduce a run rather than merely resemble it.
//
// Submitted and advanced action by action rather than submitted all at once. The queue then
// holds a single action's worth of client flow plus whatever the schedules have pending, instead
// of the entire trace, which is what lets a day's worth of it replay without being held in
// memory twice.
//
// Dispatch order is unaffected by feeding it that way. Ties are settled by kind before the
// submission counter, so client flow never reorders against a schedule transition however the
// counters fall; and two entries of the same kind keep their relative counters however many
// others were queued between them. Both orderings come out identical, which is asserted rather
// than assumed.
public static class Replay
{
    // Runs the trace, calling back for each dispatch in order.
    //
    // `until` advances once more after the last action, so a close the schedule still has pending
    // is dispatched rather than left in the queue - a trace that stops mid-session should still
    // be able to end its day. Ignored if it is not past where the trace already left the
    // sequencer, since time only runs one way.
    public static void Run(Sequencer sequencer, IEnumerable<OrderBookAction> trace,
        Action<Dispatched>? onDispatched = null, DateTime? until = null)
    {
        ArgumentNullException.ThrowIfNull(sequencer);
        ArgumentNullException.ThrowIfNull(trace);

        foreach (var action in trace)
        {
            sequencer.Submit(action);

            // To the action's own instant rather than to the end of the trace: anything the
            // schedule or an interruption has due before it is dispatched first, in its place.
            Report(sequencer.AdvanceTo(action.Time), onDispatched);
        }

        if (until is { } end && end > sequencer.LogicalNow)
            Report(sequencer.AdvanceTo(end), onDispatched);
    }

    // Runs a trace through an InstrumentGroup's sequencer, publishing every dispatch through the
    // group's channel, and returns the channel messages in order. A convenience that replaces the
    // manual wiring of Run(group.Sequencer, trace, d => ...) with a single call.
    public static IReadOnlyList<ChannelMessage> Run(InstrumentGroup group,
        IEnumerable<OrderBookAction> trace, DateTime? until = null)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(trace);

        var messages = new List<ChannelMessage>();
        Run(group.Sequencer, trace, d => messages.AddRange(group.Channel.Publish(d.Events)), until);
        return messages;
    }

    private static void Report(IReadOnlyList<Dispatched> dispatched, Action<Dispatched>? onDispatched)
    {
        if (onDispatched == null) return;

        foreach (var d in dispatched)
            onDispatched(d);
    }
}
