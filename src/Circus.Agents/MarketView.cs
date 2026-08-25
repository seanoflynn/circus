using Circus.MarketData;

namespace Circus.Agents;

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

public sealed class InstrumentView
{
    private readonly LevelBook _levels = new();

    internal InstrumentView(string symbol)
    {
        Symbol = symbol;
    }

    public string Symbol { get; }

    public DateTime Time { get; private set; }

    public IReadOnlyList<Level> Bids => _levels.Bids;

    public IReadOnlyList<Level> Offers => _levels.Offers;

    public decimal? BestBid => _levels.BestBid;

    public decimal? BestOffer => _levels.BestOffer;

    public decimal? Mid => BestBid is { } bid && BestOffer is { } offer ? (bid + offer) / 2 : null;

    public decimal? LastTradePrice { get; private set; }

    public int LastTradeQuantity { get; private set; }

    public decimal? IndicativePrice { get; private set; }

    public OrderBookStatus Status { get; private set; } = OrderBookStatus.Closed;

    public OrderBookStatusChangeReason StatusReason { get; private set; } = OrderBookStatusChangeReason.Requested;

    public DateTime? ResumesAt { get; private set; }

    public Side? LimitState { get; private set; }

    public bool IsOpen => Status == OrderBookStatus.Open;

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
        }
    }
}
