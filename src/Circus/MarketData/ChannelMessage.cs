namespace Circus.MarketData;

// One message as it leaves a channel. Sequence is that stream's own contiguous count, so a
// subscriber that sees it skip has missed something - which is the only reason to number messages
// at all.
//
// AsOfSequence is what makes the two streams reconcilable, and is the whole mechanism rather than
// a convenience: it is the incremental sequence a snapshot is consistent as of - CME carries it as
// LastMsgSeqNumProcessed - so a subscriber joining mid-session buffers the incremental stream,
// waits for a snapshot, applies it, discards the buffered messages up to and including that
// number, then applies the rest and stops reading snapshots. Zero on an incremental message, which
// is consistent as of itself.
public readonly record struct ChannelMessage(long Sequence, MarketDataEvent Data,
    ChannelStream Stream = ChannelStream.Incremental, long AsOfSequence = 0);
