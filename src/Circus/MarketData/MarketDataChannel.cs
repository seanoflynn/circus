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
    private readonly Dictionary<string, SecurityFeed> _feeds = new();

    private long _sequence;

    // What a subscriber has seen so far, so a caller resuming one can say where it got to.
    public long Sequence => _sequence;

    public void Add(SecurityFeed feed)
    {
        ArgumentNullException.ThrowIfNull(feed);

        if (!_feeds.TryAdd(feed.Symbol, feed))
            throw new ArgumentException(
                $"a feed is already registered for {feed.Symbol}", nameof(feed));
    }

    // Turns one dispatch's events into the messages this channel publishes for them.
    //
    // Events for a security this channel does not carry are ignored rather than refused: a
    // channel is deliberately a subset of the venue, and the securities it leaves out are the
    // normal case rather than a routing mistake.
    public IReadOnlyList<ChannelMessage> Publish(IReadOnlyList<OrderBookEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        if (events.Count == 0)
            return Array.Empty<ChannelMessage>();

        List<ChannelMessage>? output = null;

        foreach (var (symbol, forSecurity) in GroupBySymbol(events))
        {
            if (!_feeds.TryGetValue(symbol, out var feed))
                continue;

            foreach (var data in feed.Process(forSecurity))
            {
                output ??= new List<ChannelMessage>();
                output.Add(new ChannelMessage(++_sequence, data));
            }
        }

        return output ?? (IReadOnlyList<ChannelMessage>) Array.Empty<ChannelMessage>();
    }

    // One dispatch is one book's events today, so the common path is a single group and the whole
    // list is passed straight through. Grouped rather than assumed anyway, because an action that
    // implied a fill in another book would arrive here as events spanning securities, and each
    // book's producers must be handed their own book's events and only those.
    private static IEnumerable<(string Symbol, IReadOnlyList<OrderBookEvent> Events)> GroupBySymbol(
        IReadOnlyList<OrderBookEvent> events)
    {
        var first = events[0].Symbol;

        var spansSecurities = false;
        for (var i = 1; i < events.Count; i++)
        {
            if (events[i].Symbol == first) continue;

            spansSecurities = true;
            break;
        }

        if (!spansSecurities)
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