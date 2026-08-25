using Circus.Actions;
using Circus.MarketData;
using Circus.Sessions;

namespace Circus.Sequencing;

public sealed class InstrumentGroup
{
    private readonly Sequencer _sequencer;

    private sealed record ChannelConfig(MarketDataChannel Channel, FeedProducts Products, int Depth,
        int SnapshotEvery);

    private readonly Dictionary<string, ChannelConfig> _channels = new();
    private readonly List<string> _channelOrder = new();
    private readonly List<string> _symbols = new();

    private readonly Dictionary<string, OrderBook> _ownBooks = new();

    public InstrumentGroup(DateTime start, TimeSpan? snapshotInterval = null)
    {
        _sequencer = new Sequencer(start, snapshotInterval);
    }

    public Sequencer Sequencer => _sequencer;

    public IReadOnlyList<string> Symbols => _symbols;

    public IReadOnlyList<string> ChannelNames => _channelOrder;

    public MarketDataChannel Channel => _channelOrder.Count switch
    {
        1 => _channels[_channelOrder[0]].Channel,
        0 => throw new InvalidOperationException(
            "this group has no channels yet - declare one, or add an instrument and take the " +
            "default that comes with it"),
        _ => throw new InvalidOperationException(
            $"this group publishes {_channelOrder.Count} channels ({string.Join(", ", _channelOrder)}), " +
            "so there is no single one to take - name the one you mean")
    };

    public MarketDataChannel ChannelNamed(string name) =>
        _channels.TryGetValue(name, out var config)
            ? config.Channel
            : throw new ArgumentException($"no channel named {name} in this group", nameof(name));

    public void AddChannel(string name, FeedProducts products = FeedProducts.All,
        int depth = OrderBook.DefaultPublishedDepth, int snapshotEvery = 1)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (_channels.ContainsKey(name))
            throw new ArgumentException($"a channel named {name} is already in this group", nameof(name));

        if (depth <= 0)
            throw new ArgumentOutOfRangeException(nameof(depth), depth,
                "a channel carrying no levels is not a by-price channel");

        if (snapshotEvery <= 0)
            throw new ArgumentOutOfRangeException(nameof(snapshotEvery), snapshotEvery,
                "a channel that skips every tick has no snapshot stream - leave the group's " +
                "snapshot interval unset instead, which says so");

        var channel = new MarketDataChannel(name);
        var config = new ChannelConfig(channel, products, depth, snapshotEvery);
        _channels[name] = config;
        _channelOrder.Add(name);

        foreach (var symbol in _symbols)
        {
            if (Carries(config, FeedProducts.ByPrice) && _ownBooks.TryGetValue(symbol, out var book))
                book.AlsoReport(depth);

            channel.Add(FeedFor(symbol, config));
        }
    }

    public void Add(Instrument instrument, MarketSchedule schedule,
        IReadOnlyList<string>? channels = null)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        ArgumentNullException.ThrowIfNull(schedule);

        RequireChannelsExist(channels);
        EnsureSomeChannel(channels);

        var book = new OrderBook(instrument, DepthsFor(channels));
        _sequencer.Add(book, schedule);
        _ownBooks[instrument.Symbol] = book;
        Publish(instrument.Symbol, channels);
    }

    public void Add(IOrderBook book, MarketSchedule schedule, IReadOnlyList<string>? channels = null)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(schedule);

        RequireChannelsExist(channels);

        _sequencer.Add(book, schedule);
        Publish(book.Symbol, channels);
    }

    private int[] DepthsFor(IReadOnlyList<string>? channels)
    {
        var depths = (channels ?? _channelOrder)
            .Select(name => _channels[name])
            .Where(config => Carries(config, FeedProducts.ByPrice))
            .Select(config => config.Depth)
            .Distinct()
            .ToArray();

        return depths.Length > 0 ? depths : new[] {OrderBook.DefaultPublishedDepth};
    }

    internal IReadOnlyList<int> PublishedDepthsFor(string symbol) =>
        _ownBooks.TryGetValue(symbol, out var book)
            ? book.PublishedDepths
            : Array.Empty<int>();

    private static bool Carries(ChannelConfig config, FeedProducts product) =>
        (config.Products & product) != 0;

    private static InstrumentFeed FeedFor(string symbol, ChannelConfig config) =>
        new(symbol, config.Products, config.Depth, config.SnapshotEvery);

    private void EnsureSomeChannel(IReadOnlyList<string>? channels)
    {
        if (channels == null && _channels.Count == 0)
            AddChannel(MarketDataChannel.DefaultName);
    }

    private void RequireChannelsExist(IReadOnlyList<string>? channels)
    {
        if (channels == null) return;

        foreach (var name in channels)
        {
            if (!_channels.ContainsKey(name))
                throw new ArgumentException($"no channel named {name} in this group", nameof(channels));
        }
    }

    private void Publish(string symbol, IReadOnlyList<string>? channels)
    {
        EnsureSomeChannel(channels);

        var carrying = channels ?? _channelOrder;

        foreach (var name in carrying)
            _channels[name].Channel.Add(FeedFor(symbol, _channels[name]));

        _symbols.Add(symbol);
    }

    public void Submit(OrderBookAction action) => _sequencer.Submit(action);
}