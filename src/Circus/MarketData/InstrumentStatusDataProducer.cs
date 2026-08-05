using Circus.Events;

namespace Circus.MarketData;

// What state an instrument is in, as one thing - CME's Security Status message, Eurex's
// instrument state. The book publishes the parts separately because they are separate: a status
// change and a limit lock are different events, and a limit-locked market is open. A subscriber
// wanting to render "what is happening to this instrument" wants them together, and assembling
// them is this producer's whole job.
//
// The last producer here that accumulates anything, and it does so for a reason none of the
// others now have: each incoming event carries one part of a composite, so the rest has to be
// remembered. Depth was the same shape until the book began reporting whole level sets itself,
// and the trade print never needed more than a batch. This one cannot be fixed that way - the
// parts genuinely arrive separately, because a status change and a limit lock are separate
// things that happen at separate times.
//
// So it stays one instance per IOrderBook, created before that book processes its first action.
// The answer to a missed event is not to make this stateless but to republish the composite, the
// way CME's snapshot carries instrument status alongside the book: a subscriber that never saw
// the StatusChanged learns the status from the next snapshot rather than by replaying history it
// missed. Until that channel exists, a gap here is unrecoverable.
//
// Starts where a book starts - closed, nothing pending, no limit - so the first status change
// is a change from something true rather than from a guess.
public class InstrumentStatusDataProducer : IIncrementalProducer<InstrumentStatusDataEvent>
{
    private OrderBookStatus _status = OrderBookStatus.Closed;
    private OrderBookStatusChangeReason _reason = OrderBookStatusChangeReason.Requested;
    private DateTime? _resumesAt;
    private Side? _limitState;

    public IList<InstrumentStatusDataEvent> Process(IReadOnlyList<MarketEvent> events)
    {
        List<InstrumentStatusDataEvent>? output = null;

        foreach (var ev in events)
        {
            switch (ev)
            {
                case StatusChanged status:
                    _status = status.Status;
                    _reason = status.Reason;
                    _resumesAt = status.ResumesAt;
                    break;

                // A limit says nothing about the status, and must not disturb it: the market is
                // open, and stuck.
                case LimitStateChanged limit:
                    _limitState = limit.Side;
                    break;

                default:
                    continue;
            }

            // One per contributing event, carrying that event's own time rather than a time
            // chosen for a batch. Each is a complete picture, so a consumer never needs two.
            output ??= new List<InstrumentStatusDataEvent>();
            output.Add(new InstrumentStatusDataEvent(ev.Symbol, ev.Time, _status, _reason, _resumesAt, _limitState));
        }

        return output ?? (IList<InstrumentStatusDataEvent>) Array.Empty<InstrumentStatusDataEvent>();
    }
}
