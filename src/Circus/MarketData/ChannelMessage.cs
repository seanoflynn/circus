namespace Circus.MarketData;

public readonly record struct ChannelMessage(long Sequence, MarketDataEvent Data,
    ChannelStream Stream = ChannelStream.Incremental, long AsOfSequence = 0);
