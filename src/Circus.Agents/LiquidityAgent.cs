using Circus.Actions;
using Circus.Events;
using Circus.MarketData;

namespace Circus.Agents;

public sealed class LiquidityAgent : IAgent
{
    private static readonly Side[] Sides = {Side.Buy, Side.Sell};

    private const int MaxPrefixLength = 11;

    private readonly LiquidityAgentOptions _options;
    private readonly Random _random;
    private readonly Dictionary<string, Instrument> _instruments;
    private readonly string[] _symbols;
    private readonly string _idPrefix;
    private readonly OrderValidity _validity;
    private readonly SelfMatchPrevention? _selfMatchPrevention;

    // Two actions on one order within a tick share an instant and dispatch in the order they were
    // written, so the second would name an order the first had just retired or renamed.
    private readonly HashSet<string> _touched = new();

    private int _written;

    private long _nextId = 1;

    public LiquidityAgent(string companyId, IReadOnlyList<Instrument> instruments,
        LiquidityAgentOptions? options = null, int? seed = null, string? clientOrderIdPrefix = null)
    {
        ArgumentNullException.ThrowIfNull(instruments);

        if (string.IsNullOrEmpty(companyId))
            throw new ArgumentException("a company id is required", nameof(companyId));
        if (instruments.Count == 0)
            throw new ArgumentException("at least one instrument is required", nameof(instruments));

        _options = options ?? new LiquidityAgentOptions();
        _options.Validate();

        Seed = seed ?? Random.Shared.Next();
        _random = new Random(Seed);

        CompanyId = companyId;
        _instruments = instruments.ToDictionary(i => i.Symbol);
        _symbols = instruments.Select(i => i.Symbol).ToArray();

        _idPrefix = clientOrderIdPrefix ?? companyId;
        if (_idPrefix.Length > MaxPrefixLength)
            throw new ArgumentException(
                $"a client order id prefix of at most {MaxPrefixLength} characters leaves room for the " +
                "counter within the book's 20-character limit. Pass a shorter clientOrderIdPrefix.",
                nameof(clientOrderIdPrefix));

        _validity = _options.Validity ?? new OrderValidity.GoodTilCanceled();

        _selfMatchPrevention = _options.SelfMatchPrevention is { } instruction
            ? new SelfMatchPrevention {Id = companyId, Instruction = instruction}
            : null;
    }

    public string CompanyId { get; }

    public IReadOnlyList<string> Symbols => _symbols;

    public int Seed { get; }

    public OrderTracker Orders { get; } = new();

    public MarketView Market { get; } = new();

    public void OnMarketData(MarketDataEvent data) => Market.Apply(data);

    public void OnOwnEvent(OrderBookEvent ev) => Orders.Apply(ev);

    public IReadOnlyList<OrderBookAction> Act(DateTime now)
    {
        List<OrderBookAction>? actions = null;

        _touched.Clear();
        _written = 0;

        foreach (var symbol in _symbols)
        {
            var view = Market.Of(symbol);

            if (!view.IsOpen) continue;

            if (_random.NextDouble() >= _options.ActProbability) continue;

            var instrument = _instruments[symbol];
            var reference = Reference(instrument, view);

            Churn(ref actions, instrument, reference);
            Cross(ref actions, instrument, view);
            Quote(ref actions, instrument, reference);
        }

        return actions ?? (IReadOnlyList<OrderBookAction>) Array.Empty<OrderBookAction>();
    }

    private decimal Reference(Instrument instrument, InstrumentView view) =>
        AlignToTick(instrument, view.Mid ?? view.LastTradePrice ?? _options.ReferencePrice);

    private void Churn(ref List<OrderBookAction>? actions, Instrument instrument, decimal reference)
    {
        var live = LiveIn(instrument.Symbol);
        if (live.Count == 0) return;

        if (_random.NextDouble() < _options.CancelProbability)
        {
            var order = live[_random.Next(live.Count)];

            if (_touched.Add(order.ClientOrderId))
                Add(ref actions, new CancelOrder
                {
                    Symbol = instrument.Symbol, CompanyId = CompanyId, ClientOrderId = NextClientOrderId(),
                    PreviousClientOrderId = order.ClientOrderId
                });
        }

        if (_random.NextDouble() < _options.ReplaceProbability)
        {
            var order = live[_random.Next(live.Count)];
            var price = RungPrice(instrument, reference, order.Side, _random.Next(_options.Depth));

            if (price > 0 && price != order.Price && _touched.Add(order.ClientOrderId))
                Add(ref actions, new UpdateOrder
                {
                    Symbol = instrument.Symbol, CompanyId = CompanyId, ClientOrderId = NextClientOrderId(),
                    PreviousClientOrderId = order.ClientOrderId, Price = price
                });
        }
    }

    private void Cross(ref List<OrderBookAction>? actions, Instrument instrument, InstrumentView view)
    {
        if (_random.NextDouble() >= _options.Aggression) return;
        if (AtOrderLimit) return;

        if (PickSide(instrument.Symbol) is not { } side) return;

        var touch = side == Side.Buy ? view.BestOffer : view.BestBid;
        if (touch is not { } best) return;

        var quantity = NextQuantity();

        if (_random.NextDouble() < _options.MarketOrderProbability)
        {
            Add(ref actions, new CreateMarketOrder
            {
                Symbol = instrument.Symbol, CompanyId = CompanyId, ClientOrderId = NextClientOrderId(),
                OrderValidity = _validity, Side = side, Quantity = quantity,
                SelfMatchPrevention = _selfMatchPrevention, MaxVisibleQuantity = VisibleQuantity(quantity)
            });
        }
        else
        {
            var through = _options.SweepTicks * instrument.TickSize;
            var price = side == Side.Buy ? best + through : best - through;

            if (price <= 0) return;

            Add(ref actions, new CreateLimitOrder
            {
                Symbol = instrument.Symbol, CompanyId = CompanyId, ClientOrderId = NextClientOrderId(),
                OrderValidity = _validity, Side = side, Quantity = quantity, Price = price,
                SelfMatchPrevention = _selfMatchPrevention, MaxVisibleQuantity = VisibleQuantity(quantity)
            });
        }

        _written++;
    }

    private void Quote(ref List<OrderBookAction>? actions, Instrument instrument, decimal reference)
    {
        foreach (var side in Sides)
        {
            if (!CanAdd(instrument.Symbol, side)) continue;

            for (var rung = 0; rung < _options.Depth; rung++)
            {
                if (AtOrderLimit) return;

                var price = RungPrice(instrument, reference, side, rung);

                if (price <= 0 || HasOrderAt(instrument.Symbol, side, price)) continue;

                var quantity = NextQuantity();

                Add(ref actions, new CreateLimitOrder
                {
                    Symbol = instrument.Symbol, CompanyId = CompanyId, ClientOrderId = NextClientOrderId(),
                    OrderValidity = _validity, Side = side, Quantity = quantity, Price = price,
                    SelfMatchPrevention = _selfMatchPrevention, MaxVisibleQuantity = VisibleQuantity(quantity)
                });

                _written++;
            }
        }
    }

    // Rung 0 sits one spacing off the reference rather than on it, so the agent's own bid and offer
    // are always a rung apart and it never quotes itself into a trade.
    private decimal RungPrice(Instrument instrument, decimal reference, Side side, int rung)
    {
        var offset = (rung + 1) * _options.LevelSpacingTicks * instrument.TickSize;
        return side == Side.Buy ? reference - offset : reference + offset;
    }

    private bool AtOrderLimit => Orders.LiveCount + _written >= _options.MaxLiveOrders;

    // A draw is consumed only when both sides are open, so a limited agent stays reproducible.
    private Side? PickSide(string symbol)
    {
        var canBuy = CanAdd(symbol, Side.Buy);
        var canSell = CanAdd(symbol, Side.Sell);

        if (canBuy && canSell) return _random.Next(2) == 0 ? Side.Buy : Side.Sell;
        if (canBuy) return Side.Buy;
        if (canSell) return Side.Sell;

        return null;
    }

    private bool CanAdd(string symbol, Side side)
    {
        if (_options.MaxPosition is not { } max) return true;

        var position = Orders.Position(symbol);
        return side == Side.Buy ? position < max : position > -max;
    }

    private bool HasOrderAt(string symbol, Side side, decimal price)
    {
        foreach (var order in Orders.LiveOrders)
        {
            if (order.Symbol == symbol && order.Side == side && order.Price == price)
                return true;
        }

        return false;
    }

    private List<LiveOrder> LiveIn(string symbol)
    {
        var live = new List<LiveOrder>();

        foreach (var order in Orders.LiveOrders)
        {
            if (order.Symbol == symbol)
                live.Add(order);
        }

        return live;
    }

    private int NextQuantity() => _random.Next(_options.MinQuantity, _options.MaxQuantity + 1);

    private int? VisibleQuantity(int quantity) =>
        _options.MaxVisibleQuantity is { } visible ? Math.Min(visible, quantity) : null;

    private string NextClientOrderId() => $"{_idPrefix}{_nextId++}";

    private static decimal AlignToTick(Instrument instrument, decimal price) =>
        Math.Round(price / instrument.TickSize) * instrument.TickSize;

    private static void Add(ref List<OrderBookAction>? actions, OrderBookAction action) =>
        (actions ??= new List<OrderBookAction>()).Add(action);
}
