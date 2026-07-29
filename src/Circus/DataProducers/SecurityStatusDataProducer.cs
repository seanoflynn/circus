using Circus.OrderBook;
using Circus.OrderBook.Events;

namespace Circus.DataProducers;

// What state an instrument is in, as one thing - CME's Security Status message, Eurex's
// instrument state. The book publishes the parts separately because they are separate: a status
// change and a limit lock are different events, and a limit-locked market is open. A subscriber
// wanting to render "what is happening to this instrument" wants them together, and assembling
// them is this producer's whole job.
//
// Stateful, like the level producers and unlike the trade and indicative ones: each incoming
// event carries only its own part, so the rest has to be remembered. That means one instance per
// IOrderBook, created before that book processes its first action, with no way to resync after a
// missed event.
//
// Starts where a book starts - closed, nothing pending, no limit - so the first status change
// is a change from something true rather than from a guess.
public class SecurityStatusDataProducer : IDataProducer<SecurityStatusDataEvent>
{
    private OrderBookStatus _status = OrderBookStatus.Closed;
    private StatusChangeReason _reason = StatusChangeReason.Requested;
    private DateTime? _resumesAt;
    private Side? _limitState;

    public IList<SecurityStatusDataEvent> Process(IOrderBook book, IReadOnlyList<OrderBookEvent> events)
    {
        List<SecurityStatusDataEvent>? output = null;

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
            output ??= new List<SecurityStatusDataEvent>();
            output.Add(new SecurityStatusDataEvent(ev.Time, _status, _reason, _resumesAt, _limitState));
        }

        return output ?? (IList<SecurityStatusDataEvent>) Array.Empty<SecurityStatusDataEvent>();
    }
}

// ResumesAt is when a timed interruption is due back, null when nothing is pending. LimitState
// is which way a daily limit has the market stuck - Buy for limit up, where buyers cannot push
// higher - and null when it is free to trade.
public record SecurityStatusDataEvent(DateTime Time, OrderBookStatus Status, StatusChangeReason Reason,
    DateTime? ResumesAt, Side? LimitState);
