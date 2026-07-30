using Circus.Actions;
using Circus.Events;
using Circus.MarketData;

namespace Circus.Simulator;

// Generates a plausible, replayable stream of order book actions (create/cancel/update,
// limit/market) across one or more securities. Drives a private "shadow" book per security
// purely to keep track of which orders are currently live, so that generated cancels/updates
// always target a real resting order instead of a random, possibly-already-filled id.
//
// Several securities produce one interleaved trace rather than one trace each: a venue's flow
// arrives mixed, and a sequencer being fed this is exactly the thing that has to put it back in
// order. Each security's shadow book, level view and live-order set are its own, so flow on one
// never depends on what another was doing - the same independence the real books have.
//
// Pass a seed for a deterministic, reproducible trace (e.g. a committed benchmark baseline);
// omit it to get a fresh random trace each run (e.g. fuzzing).
public sealed class OrderFlowSimulator
{
    private readonly SimulatorOptions _options;
    private readonly Random _random;
    private readonly int _seed;

    private readonly List<BookState> _books;

    // deterministic (derived from the seeded action sequence, not Guid.NewGuid()) so traces
    // stay reproducible for a given seed. Shared across securities so no two orders anywhere in
    // a trace collide on a client order id.
    private long _nextId;

    // A fixed epoch rather than DateTime.UtcNow, and a fixed step per action: a trace is
    // supposed to be reproducible from its seed alone, and stamping it from the wall clock
    // would have made the times - and so any GTD or rolling-window behaviour reading them -
    // differ on every run of the same seed.
    private static readonly DateTime Epoch = new(2000, 1, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(1);
    private DateTime _time = Epoch;

    public OrderFlowSimulator(Security security, SimulatorOptions? options = null, int? seed = null)
        : this(new[] {security}, options, seed)
    {
    }

    public OrderFlowSimulator(IReadOnlyList<Security> securities, SimulatorOptions? options = null,
        int? seed = null)
    {
        if (securities.Count == 0)
            throw new ArgumentException("at least one security is required", nameof(securities));

        _options = options ?? new SimulatorOptions();
        _seed = seed ?? Random.Shared.Next();
        _random = new Random(_seed);

        _books = securities.Select(s => new BookState(s, _time)).ToList();
    }

    public int Seed => _seed;

    public IReadOnlyList<Security> Securities => _books.Select(b => b.Security).ToList();

    // Generates the next `actionCount` actions, continuing from wherever the internal shadow
    // books currently are (i.e. calling this repeatedly extends the same session rather than
    // restarting it). The returned list contains only the newly generated batch, not the full
    // history.
    public IReadOnlyList<OrderBookAction> Generate(int actionCount)
    {
        var actions = new List<OrderBookAction>(actionCount);

        for (var i = 0; i < actionCount; i++)
        {
            // Stamped here rather than in each Build* method, so every action gets one and the
            // trace a caller receives is self-contained - replayable without a clock beside it.
            _time += Step;

            // Not drawn at all when there is only one book to draw. A draw consumes the seeded
            // sequence, so making it unconditionally would have shifted every trace this class
            // has ever produced for a single security - including the committed benchmark
            // baseline - for no reason beyond tidiness.
            var book = _books.Count == 1 ? _books[0] : _books[_random.Next(_books.Count)];
            var action = NextAction(book) with {Time = _time};

            actions.Add(action);
            book.Apply(action);
        }

        return actions;
    }

    private string NextCompanyId() => $"c{_nextId++}";
    private string NextClientOrderId() => $"o{_nextId++}";

    private OrderBookAction NextAction(BookState book)
    {
        var hasLive = book.HasLive;
        var roll = _random.NextDouble();

        if (hasLive)
        {
            if (roll < _options.CancelWeight)
                return BuildCancel(book);
            roll -= _options.CancelWeight;

            if (roll < _options.UpdateWeight)
                return BuildUpdate(book);
            roll -= _options.UpdateWeight;
        }

        if (roll < _options.MarketOrderWeight)
            return BuildCreateMarket(book);

        return BuildCreateLimit(book);
    }

    private CreateLimitOrder BuildCreateLimit(BookState book)
    {
        var side = _random.Next(2) == 0 ? Side.Buy : Side.Sell;
        var price = ComputePrice(book, side);
        var quantity = _random.Next(_options.MinQuantity, _options.MaxQuantity + 1);

        return new CreateLimitOrder
        {
            Security = book.Security, CompanyId = NextCompanyId(), ClientOrderId = NextClientOrderId(),
            OrderValidity = new OrderValidity.GoodTilCanceled(), Side = side, Quantity = quantity, Price = price
        };
    }

    private CreateMarketOrder BuildCreateMarket(BookState book)
    {
        var side = _random.Next(2) == 0 ? Side.Buy : Side.Sell;
        var quantity = _random.Next(_options.MinQuantity, _options.MaxQuantity + 1);

        return new CreateMarketOrder
        {
            Security = book.Security, CompanyId = NextCompanyId(), ClientOrderId = NextClientOrderId(),
            OrderValidity = new OrderValidity.GoodTilCanceled(), Side = side, Quantity = quantity
        };
    }

    private CancelOrder BuildCancel(BookState book)
    {
        var info = book.PickLive(_random);
        return new CancelOrder
        {
            Security = book.Security, CompanyId = info.CompanyId, ClientOrderId = NextClientOrderId(),
            PreviousClientOrderId = info.ClientOrderId
        };
    }

    private UpdateOrder BuildUpdate(BookState book)
    {
        var info = book.PickLive(_random);
        var newClientOrderId = NextClientOrderId();

        // stop orders keep a Price/TriggerPrice relationship that a blind tweak could
        // violate, so only ever adjust their quantity.
        var updateQuantityOnly = info.TriggerPrice.HasValue;
        if (updateQuantityOnly || _random.NextDouble() < 0.5)
        {
            var quantity = _random.Next(_options.MinQuantity, _options.MaxQuantity + 1);
            return new UpdateOrder
            {
                Security = book.Security, CompanyId = info.CompanyId, ClientOrderId = newClientOrderId,
                PreviousClientOrderId = info.ClientOrderId, NewTotalQuantity = quantity
            };
        }

        var tick = book.Security.TickSize;
        var direction = info.Side == Side.Buy ? -1 : 1; // move away from touch, staying passive
        var reference = info.Price ?? AlignToTick(book.Security, _options.StartingPrice);
        var newPrice = reference + direction * _random.Next(1, 4) * tick;

        return new UpdateOrder
        {
            Security = book.Security, CompanyId = info.CompanyId, ClientOrderId = newClientOrderId,
            PreviousClientOrderId = info.ClientOrderId, Price = newPrice
        };
    }

    private decimal ComputePrice(BookState book, Side side)
    {
        var tick = book.Security.TickSize;
        var bestBuy = book.Levels.Bids;
        var bestSell = book.Levels.Offers;

        if (bestBuy.Count == 0 && bestSell.Count == 0)
            return AlignToTick(book.Security, _options.StartingPrice);

        var opposite = side == Side.Buy ? bestSell : bestBuy;
        var own = side == Side.Buy ? bestBuy : bestSell;

        if (opposite.Count > 0 && _random.NextDouble() < _options.CrossProbability)
        {
            var throughTicks = _random.Next(0, 3);
            return side == Side.Buy
                ? opposite[0].Price + throughTicks * tick
                : opposite[0].Price - throughTicks * tick;
        }

        var reference = own.Count > 0 ? own[0].Price : opposite[0].Price;
        var offsetTicks = _random.Next(0, _options.PriceRangeTicks + 1);

        return side == Side.Buy ? reference - offsetTicks * tick : reference + offsetTicks * tick;
    }

    private static decimal AlignToTick(Security security, decimal price)
    {
        var tick = security.TickSize;
        return Math.Round(price / tick) * tick;
    }

    // One security's worth of the modelling the simulator needs to generate flow that targets
    // real resting orders: a shadow book, the touch rebuilt from its events, and the set of
    // orders currently live in it.
    //
    // Not a venue in miniature - no schedule, no sequencer. The book here is a tracking device
    // for "what is still resting", opened once and never moved through a session again, so
    // routing it through a sequencer would buy nothing and cost the simulator a schedule it has
    // no opinion about.
    private sealed class BookState
    {
        private readonly OrderBook _shadowBook;

        // The touch, rebuilt from the shadow book's events the same way any market data consumer
        // would - the book itself answers no questions about its levels.
        private readonly LevelDataProducer _touch = new(1);

        // Keyed by ClientOrderId, not ExchangeOrderId - the latter no longer stays constant for
        // an order's whole life (a reprice, a quantity increase, or an iceberg peak refilling
        // from its hidden reserve all mint a fresh one), so it can't be used as a stable tracking
        // key here. ClientOrderId changes too (a new one is chosen on every update/cancel), but
        // the simulator is itself the one choosing each new value, so it renames its own tracking
        // entries in lockstep rather than needing a truly immutable id.
        private readonly List<string> _liveIds = new();
        private readonly Dictionary<string, int> _liveIndex = new();
        private readonly Dictionary<string, LiveOrderInfo> _liveInfo = new();

        public BookState(Security security, DateTime openedAt)
        {
            Security = security;
            Levels = new LevelsDataEvent(security, default, Array.Empty<Level>(), Array.Empty<Level>());
            _shadowBook = new OrderBook(security);
            _shadowBook.Process(new OpenTrading {Security = security, Time = openedAt});
        }

        public Security Security { get; }

        public LevelsDataEvent Levels { get; private set; }

        public bool HasLive => _liveIds.Count > 0;

        public LiveOrderInfo PickLive(Random random) => _liveInfo[_liveIds[random.Next(_liveIds.Count)]];

        public void Apply(OrderBookAction action)
        {
            var events = _shadowBook.Process(action);

            foreach (var levels in _touch.Process(events))
                Levels = levels;

            foreach (var e in events)
            {
                switch (e)
                {
                    case CreateOrderConfirmed c:
                        Track(c.Order, previousClientOrderId: null);
                        break;
                    case UpdateOrderConfirmed u:
                        Track(u.Order, u.PreviousClientOrderId);
                        break;
                    case CancelOrderConfirmed cancel:
                        RemoveLive(cancel.PreviousClientOrderId);
                        break;
                    case ExpireOrderConfirmed expire:
                        RemoveLive(expire.Order.ClientOrderId);
                        break;
                    case OrdersMatched matched:
                        foreach (var fill in matched.Fills)
                        {
                            if (fill.Order.RemainingQuantity == 0)
                                RemoveLive(fill.Order.ClientOrderId);
                        }
                        break;
                }
            }
        }

        // previousClientOrderId is null for a fresh Create, and the id being renamed from for an
        // Update - renaming (rather than remove-then-add under whatever key happens to already
        // be there) keeps a single live entry across the chain of client order ids one logical
        // resting order accumulates over its life.
        private void Track(Order order, string? previousClientOrderId)
        {
            if (order.RemainingQuantity == 0)
            {
                RemoveLive(previousClientOrderId ?? order.ClientOrderId);
                return;
            }

            if (previousClientOrderId != null && previousClientOrderId != order.ClientOrderId)
                RemoveLive(previousClientOrderId);

            if (!_liveIndex.ContainsKey(order.ClientOrderId))
            {
                _liveIndex[order.ClientOrderId] = _liveIds.Count;
                _liveIds.Add(order.ClientOrderId);
            }

            _liveInfo[order.ClientOrderId] = new LiveOrderInfo(order.CompanyId, order.ClientOrderId,
                order.Side, order.Price, order.TriggerPrice);
        }

        private void RemoveLive(string clientOrderId)
        {
            if (!_liveIndex.TryGetValue(clientOrderId, out var index))
                return;

            var lastIndex = _liveIds.Count - 1;
            var lastId = _liveIds[lastIndex];
            _liveIds[index] = lastId;
            _liveIndex[lastId] = index;
            _liveIds.RemoveAt(lastIndex);

            _liveIndex.Remove(clientOrderId);
            _liveInfo.Remove(clientOrderId);
        }
    }

    private readonly record struct LiveOrderInfo(string CompanyId, string ClientOrderId, Side Side,
        decimal? Price, decimal? TriggerPrice);
}
