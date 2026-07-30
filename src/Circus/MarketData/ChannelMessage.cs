namespace Circus.MarketData;

// One message as it leaves a channel. Sequence is that channel's own contiguous count, so a
// subscriber that sees it skip has missed something - which is the only reason to number
// messages at all.
public readonly record struct ChannelMessage(long Sequence, MarketDataEvent Data);
