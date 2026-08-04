using Circus.MarketData;

namespace Circus.Agents;

// What a subscriber knows about the instruments it follows, kept up to date from the feed.
//
// The counterpart to OrderTracker: that one is the private half of what a participant knows, this
// is the public half. Between them an agent has everything it can legitimately see, and there is
// nowhere else for it to look - no book to query, no venue internals to reach into.
//
// Views are created on first mention rather than registered up front, so an agent asking about an
// instrument that has not published yet gets an empty view - no prices, closed - rather than an
// exception. That is also the truthful answer: a subscriber that has heard nothing knows nothing.
public sealed class MarketView
{
    private readonly Dictionary<string, InstrumentView> _views = new();

    public InstrumentView this[string symbol] => Of(symbol);

    public InstrumentView Of(string symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);

        if (!_views.TryGetValue(symbol, out var view))
            _views[symbol] = view = new InstrumentView(symbol);

        return view;
    }

    public void Apply(MarketDataEvent data)
    {
        ArgumentNullException.ThrowIfNull(data);

        Of(data.Symbol).Apply(data);
    }
}

// One instrument's worth of that view.
//
// Depth is whatever the feed carries, which is the cap a real participant trades under too: an
// agent quoting ten deep off a five-deep feed is guessing about the other five, and should be.
public sealed class InstrumentView
{
    // Depth is rebuilt from the incremental feed rather than handed over whole, because that is
    // what the feed carries: the venue publishes each level as it moves, and a subscriber wanting
    // a ladder keeps one. Aggregation belongs on this side of the wire.
    private readonly LevelBook _levels = new();

    internal InstrumentView(string symbol)
    {
        Symbol = symbol;
    }

    public string Symbol { get; }

    // The instant of the last message applied, which is what this view is current as of. Not a
    // clock reading: an agent's sense of time comes from the venue, like everything else it knows.
    public DateTime Time { get; private set; }

    public IReadOnlyList<Level> Bids => _levels.Bids;

    public IReadOnlyList<Level> Offers => _levels.Offers;

    public decimal? BestBid => _levels.BestBid;

    public decimal? BestOffer => _levels.BestOffer;

    // Null unless both sides are quoting. A one-sided book has no mid, and inventing one from the
    // side that is there would be an agent's own assumption rather than something the feed said.
    public decimal? Mid => BestBid is { } bid && BestOffer is { } offer ? (bid + offer) / 2 : null;

    public decimal? LastTradePrice { get; private set; }

    public int LastTradeQuantity { get; private set; }

    // What the current phase would print if it ended now, from the auction quote a pre-open or a
    // pause publishes. Null once the phase quoting it ends, which is the withdrawal of the quote
    // rather than a gap in it.
    public decimal? IndicativePrice { get; private set; }

    // Closed until the venue says otherwise, which is where a book starts.
    public OrderBookStatus Status { get; private set; } = OrderBookStatus.Closed;

    public OrderBookStatusChangeReason StatusReason { get; private set; } = OrderBookStatusChangeReason.Requested;

    public DateTime? ResumesAt { get; private set; }

    // Which way a daily limit has the market stuck - Buy for limit up, where buyers cannot push
    // higher - and null when it is free to trade. A limit-locked market is open and trading, so
    // this is deliberately not part of Status.
    public Side? LimitState { get; private set; }

    public bool IsOpen => Status == OrderBookStatus.Open;

    // Every phase but Closed takes order actions, so an agent can still manage what it is holding
    // through a pause or a halt. Only Open trades continuously, which is a different question -
    // see IsOpen - and market orders are only accepted there.
    public bool AcceptsOrders => Status != OrderBookStatus.Closed;

    internal void Apply(MarketDataEvent data)
    {
        Time = data.Time;

        switch (data)
        {
            case MarketByPriceDeltaEvent level:
                _levels.Apply(level);
                break;

            case TradeDataEvent trade:
                LastTradePrice = trade.Price;
                LastTradeQuantity = trade.Quantity;
                break;

            case InstrumentStatusDataEvent status:
                Status = status.Status;
                StatusReason = status.Reason;
                ResumesAt = status.ResumesAt;
                LimitState = status.LimitState;
                break;

            case IndicativePriceDataEvent indicative:
                IndicativePrice = indicative.Price;
                break;

            // Order-by-order deltas are carried on the same feed and ignored here: this view is
            // aggregated depth, and an agent wanting to follow individual queue positions would
            // keep its own state off those rather than have it half-kept here.
        }
    }
}
