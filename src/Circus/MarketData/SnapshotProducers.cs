using Circus.Events;

namespace Circus.MarketData;

// The snapshot feed, as a translation of the BookSnapshot the book answers a snapshot tick with.
//
// Each of these republishes exactly the message type its incremental counterpart publishes, so a
// subscriber applies a snapshot the same way it applies an update and needs no second code path -
// the difference is which stream it arrived on and the sequence it declares itself consistent as
// of, both of which the channel carries. CME draws it the same way: the snapshot is a different
// message on a different feed, carrying the same fields.
//
// One class per product rather than one producing all three, because a channel carrying only
// depth should be able to leave the rest out - the same reason InstrumentFeed is a bundle of
// producers rather than one that does everything.
public class MarketByPriceSnapshotProducer : IIncrementalProducer<LevelsDataEvent>
{
    public IList<LevelsDataEvent> Process(IReadOnlyList<OrderBookEvent> events)
    {
        List<LevelsDataEvent>? output = null;

        foreach (var ev in events)
        {
            if (ev is not BookSnapshot snapshot)
                continue;

            output ??= new List<LevelsDataEvent>();
            output.Add(new LevelsDataEvent(snapshot.Symbol, snapshot.Time, snapshot.Bids, snapshot.Offers));
        }

        return output ?? (IList<LevelsDataEvent>) Array.Empty<LevelsDataEvent>();
    }
}

// The composite a joiner cannot rebuild: a status change and a limit lock arrive as separate
// events at separate times, so a subscriber that heard neither has no way to assemble them. This
// is what makes InstrumentStatusDataProducer's accumulator recoverable rather than merely stateful.
public class InstrumentStatusSnapshotProducer : IIncrementalProducer<InstrumentStatusDataEvent>
{
    public IList<InstrumentStatusDataEvent> Process(IReadOnlyList<OrderBookEvent> events)
    {
        List<InstrumentStatusDataEvent>? output = null;

        foreach (var ev in events)
        {
            if (ev is not BookSnapshot snapshot)
                continue;

            output ??= new List<InstrumentStatusDataEvent>();
            output.Add(new InstrumentStatusDataEvent(snapshot.Symbol, snapshot.Time, snapshot.Status,
                snapshot.StatusReason, snapshot.ResumesAt, snapshot.LimitState));
        }

        return output ?? (IList<InstrumentStatusDataEvent>) Array.Empty<InstrumentStatusDataEvent>();
    }
}

// Published on the cycle even when there is no quote, so a null price restates that the book is
// not crossing rather than leaving a joiner to assume it. The incremental feed says the same thing
// by emitting a withdrawal, which is a message a late subscriber never heard.
public class IndicativePriceSnapshotProducer : IIncrementalProducer<IndicativePriceDataEvent>
{
    public IList<IndicativePriceDataEvent> Process(IReadOnlyList<OrderBookEvent> events)
    {
        List<IndicativePriceDataEvent>? output = null;

        foreach (var ev in events)
        {
            if (ev is not BookSnapshot snapshot)
                continue;

            output ??= new List<IndicativePriceDataEvent>();
            output.Add(new IndicativePriceDataEvent(snapshot.Symbol, snapshot.Time,
                snapshot.IndicativePrice, snapshot.IndicativeQuantity));
        }

        return output ?? (IList<IndicativePriceDataEvent>) Array.Empty<IndicativePriceDataEvent>();
    }
}
