using Circus.Events;

namespace Circus.MarketData;

public sealed class MarketDataChannel
{
    private readonly Dictionary<string, InstrumentFeed> _feeds = new();

    private long _sequence;
    private long _snapshotSequence;

    public MarketDataChannel(string name = DefaultName)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public const string DefaultName = "default";

    public string Name { get; }

    public long Sequence => _sequence;

    public long SnapshotSequence => _snapshotSequence;

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

            // Stamped with the incremental sequence as it stands once this dispatch's incrementals are
            // published: that number is what a joining subscriber discards its buffer up to.
            foreach (var data in feed.Snapshot(forInstrument))
            {
                output ??= new List<ChannelMessage>();
                output.Add(new ChannelMessage(++_snapshotSequence, data, ChannelStream.Snapshot,
                    _sequence));
            }
        }

        return output ?? (IReadOnlyList<ChannelMessage>) Array.Empty<ChannelMessage>();
    }

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