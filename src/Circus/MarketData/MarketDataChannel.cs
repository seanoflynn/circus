using Circus.Events;

namespace Circus.MarketData;

// A feed carrying several instruments under one sequence, and the unit whose ordering is
// actually guaranteed.
//
// Within a channel, messages are published in the order the venue dispatched the actions behind
// them; across channels nothing is promised, which is what lets channels be published
// independently rather than through one path the whole venue queues behind. Real venues draw the
// line in the same place - CME assigns instruments to channels each with its own MsgSeqNum,
// Nasdaq ITCH runs one sequenced stream carrying every symbol - and it is why a product complex
// belongs on one channel: a spread and its legs need a common order the moment implied pricing
// exists, and two channels do not give them one.
//
// The sequence is this channel's own count of messages, not the venue's dispatch count. That is
// the whole point of it. A channel carrying three instruments out of a hundred would see the
// venue's numbering jump constantly, so a subscriber counting it could never tell a filtered-out
// dispatch from a lost message - whereas a contiguous per-channel count means a gap is loss and
// nothing else.
//
// Single-threaded, like the sequencer that feeds it: one channel, one publishing thread.
public sealed class MarketDataChannel
{
    // Keyed on the symbol rather than the record, for the reason the sequencer's routing table is:
    // two Instrument records describing the same contract need not be equal, since the restriction
    // list on them compares by reference.
    private readonly Dictionary<string, InstrumentFeed> _feeds = new();

    private long _sequence;
    private long _snapshotSequence;

    public MarketDataChannel(string name = DefaultName)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    // What a venue with more than one of these calls it. CME names its channels by product group,
    // Eurex by interface; either way a subscriber picks one by name, so having one is what lets a
    // venue's shape be written down rather than assembled by position.
    public const string DefaultName = "default";

    public string Name { get; }

    // What a subscriber has seen so far, so a caller resuming one can say where it got to.
    public long Sequence => _sequence;

    // The snapshot stream's own count, numbered apart from the incremental one. A subscriber in
    // sync never reads it, and would otherwise watch its sequence jump by a cycle's worth of
    // messages it deliberately ignored - a gap indistinguishable from a loss, which is the one
    // thing numbering is for.
    public long SnapshotSequence => _snapshotSequence;

    // What this channel carries, in the order it was given them. A channel is a subset of the
    // venue, so knowing which subset is part of describing it.
    public IReadOnlyList<string> Symbols => _symbols;

    private readonly List<string> _symbols = new();

    public void Add(InstrumentFeed feed)
    {
        ArgumentNullException.ThrowIfNull(feed);

        if (!_feeds.TryAdd(feed.Symbol, feed))
            throw new ArgumentException(
                $"a feed is already registered for {feed.Symbol}", nameof(feed));

        _symbols.Add(feed.Symbol);
    }

    // Turns one dispatch's events into the messages this channel publishes for them.
    //
    // Events for an instrument this channel does not carry are ignored rather than refused: a
    // channel is deliberately a subset of the venue, and the instruments it leaves out are the
    // normal case rather than a routing mistake.
    public IReadOnlyList<ChannelMessage> Publish(IReadOnlyList<OrderBookEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        if (events.Count == 0)
            return Array.Empty<ChannelMessage>();

        List<ChannelMessage>? output = null;

        foreach (var (symbol, forInstrument) in GroupBySymbol(events))
        {
            if (!_feeds.TryGetValue(symbol, out var feed))
                continue;

            foreach (var data in feed.Process(forInstrument))
            {
                output ??= new List<ChannelMessage>();
                output.Add(new ChannelMessage(++_sequence, data));
            }

            // After the incrementals of the same dispatch, and stamped with the incremental
            // sequence as it stands once they are published. That number is the whole mechanism:
            // it is what a joining subscriber discards its buffer up to, and it can only be right
            // if the snapshot is numbered after everything it already reflects.
            foreach (var data in feed.Snapshot(forInstrument))
            {
                output ??= new List<ChannelMessage>();
                output.Add(new ChannelMessage(++_snapshotSequence, data, ChannelStream.Snapshot,
                    _sequence));
            }
        }

        return output ?? (IReadOnlyList<ChannelMessage>) Array.Empty<ChannelMessage>();
    }

    // One dispatch is one book's events today, so the common path is a single group and the whole
    // list is passed straight through. Grouped rather than assumed anyway, because an action that
    // implied a fill in another book would arrive here as events spanning instruments, and each
    // book's feed must be handed its own book's events and only those.
    private static IEnumerable<(string Symbol, IReadOnlyList<OrderBookEvent> Events)> GroupBySymbol(
        IReadOnlyList<OrderBookEvent> events)
    {
        var first = events[0].Symbol;

        var spansInstruments = false;
        for (var i = 1; i < events.Count; i++)
        {
            if (events[i].Symbol == first) continue;

            spansInstruments = true;
            break;
        }

        if (!spansInstruments)
            return new[] {(first, events)};

        // Grouped in order of first appearance, so a channel's output order follows the events
        // rather than a dictionary's.
        var groups = new Dictionary<string, List<OrderBookEvent>>();
        var order = new List<string>();

        foreach (var ev in events)
        {
            var symbol = ev.Symbol;
            if (!groups.TryGetValue(symbol, out var list))
            {
                groups[symbol] = list = new List<OrderBookEvent>();
                order.Add(symbol);
            }

            list.Add(ev);
        }

        return order.Select(name => (name, (IReadOnlyList<OrderBookEvent>) groups[name]));
    }
}