namespace Circus.MarketData;

// Publishes the current state of one instrument, read from the book rather than derived from its
// events - the other half of the split described on IIncrementalProducer.
//
// asOfSequence is the incremental sequence number this snapshot is consistent as of, and is the
// whole mechanism rather than a convenience: two independently published streams can only be
// reconciled if the snapshot says where in the other one it stands. It is what CME carries as
// LastMsgSeqNumProcessed, and a subscriber joining mid-session uses it exactly as it does there -
// buffer the incremental feed, wait for a snapshot, apply it, discard the buffered messages up to
// and including asOfSequence, then apply the rest and stop reading snapshots.
//
// Returns one message rather than a list: a snapshot is a single complete picture by definition,
// where an incremental batch may produce any number of changes or none.
public interface ISnapshotProducer<out T> where T : MarketDataEvent
{
    T Snapshot(IBookView book, DateTime time, long asOfSequence);
}
