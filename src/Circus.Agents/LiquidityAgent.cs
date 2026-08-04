using Circus.Actions;
using Circus.Events;
using Circus.MarketData;

namespace Circus.Agents;

// The seeded workhorse: quotes a ladder each side of where it thinks the market is, lets it decay,
// and occasionally crosses. Give it a seed and it produces the same flow every time, which is what
// a benchmark baseline or a failing test wants; leave the seed out and every run differs, which is
// what fuzzing wants.
//
// It reaches for nothing but its own MarketView and OrderTracker - the feed it subscribed to and
// the events for its own orders. There is no book here, shadow or otherwise, so the agent cannot
// know something the venue has not told it and cannot disagree with the venue about what it is
// holding.
//
// Each tick, per instrument, in this order:
//
//     churn   retire one live order, and move one to a fresh rung
//     cross   with probability Aggression, take the other side
//     quote   fill whichever rungs of its ladder are empty
//
// Everything each step decides is read from the tracker, which holds what the venue has confirmed
// and not what this tick has just written. So a rung freed by this tick's cancel is refilled on
// the next tick rather than this one, and an order this tick crossed with is still counted as
// resting until the fill comes back. That lag is the honest one: it is exactly what a participant
// knows at the moment it decides.
//
// One Random for the whole agent rather than one per instrument. Draws therefore interleave across
// instruments, so a trace covering several is not the same as several traces laid side by side -
// which is exactly what the simulator did too, and what a single participant trading a book of
// products actually looks like.
public sealed class LiquidityAgent : IAgent
{
    // A fixed order, so which side is considered first is a property of the code rather than of
    // however an enum happened to be iterated.
    private static readonly Side[] Sides = {Side.Buy, Side.Sell};

    // The book allows 20 characters for a client order id, and the rest is a counter. Eleven
    // leaves room for more orders than any run will produce.
    private const int MaxPrefixLength = 11;

    private readonly LiquidityAgentOptions _options;
    private readonly Random _random;
    private readonly Dictionary<string, Instrument> _instruments;
    private readonly string[] _symbols;
    private readonly string _idPrefix;
    private readonly OrderValidity _validity;
    private readonly SelfMatchPrevention? _selfMatchPrevention;

    // Cleared at the start of every Act. Two actions on one order within a tick share an instant
    // and dispatch in the order they were written, so the second would name an order the first
    // had just retired or renamed - a rejection the agent brought on itself.
    private readonly HashSet<string> _touched = new();

    // Reset each Act, and counted alongside what the tracker already holds: orders written this
    // tick are not live yet, but they are about to be, and a limit that ignored them would be
    // exceeded by every tick that wrote more than one.
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

        // Ids need only be unique within a company, since that is how the book keys them - so a
        // counter is enough for one agent, and the prefix is what keeps two agents sharing a
        // company from writing the same id.
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

    // What the run can be reproduced from, whether it was given or drawn.
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

            // Only while the instrument is trading continuously. Pre-open, a pause and a halt all
            // take order actions too, but quoting into an auction is a different job with
            // different risk, and an agent doing it by default would be making that call for
            // whoever built it.
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

    // Where the agent thinks the market is: the mid if both sides are quoting, else the last
    // print, else what it was told to assume. Its own orders are part of that mid, which is
    // correct - a participant reading the feed cannot see which of the depth is its own, and
    // should not be pricing off a view nobody else has.
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

            // An update that changes nothing is refused, and so is one naming an order this tick
            // has already retired. Both are the agent's own doing, so both are checked here
            // rather than heard about from the venue.
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

        // Nothing to take. A market order here would be refused for the same reason, so neither
        // kind is written.
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

                // A rung the agent is already standing on needs nothing, and a rung that has
                // walked down to zero is not a price.
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

    // Rung 0 is one spacing off the reference rather than on it, so the agent's own bid and offer
    // are always a rung apart and it never quotes itself into a trade.
    private decimal RungPrice(Instrument instrument, decimal reference, Side side, int rung)
    {
        var offset = (rung + 1) * _options.LevelSpacingTicks * instrument.TickSize;
        return side == Side.Buy ? reference - offset : reference + offset;
    }

    private bool AtOrderLimit => Orders.LiveCount + _written >= _options.MaxLiveOrders;

    // Which side to take, given what the position limit still allows. A draw is consumed only
    // when both sides are open, so a limited agent's remaining choices stay reproducible.
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

    // A peak larger than the order itself is refused, so an agent quoting small and showing
    // large shows all of it instead.
    private int? VisibleQuantity(int quantity) =>
        _options.MaxVisibleQuantity is { } visible ? Math.Min(visible, quantity) : null;

    private string NextClientOrderId() => $"{_idPrefix}{_nextId++}";

    private static decimal AlignToTick(Instrument instrument, decimal price) =>
        Math.Round(price / instrument.TickSize) * instrument.TickSize;

    private static void Add(ref List<OrderBookAction>? actions, OrderBookAction action) =>
        (actions ??= new List<OrderBookAction>()).Add(action);
}
