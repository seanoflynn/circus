namespace Circus.MarketData;

// Which of a channel's two streams a message belongs to. They are numbered independently, because
// a subscriber in sync reads only the first and would otherwise see its sequence jump every cycle
// - and a gap it cannot tell from a loss is worth nothing.
public enum ChannelStream
{
    // What changed. Contiguous, and the stream a subscriber counts to know it has missed nothing.
    Incremental,

    // Where the book is. Published on the venue's snapshot cycle, and read only by a subscriber
    // joining or recovering.
    Snapshot
}
