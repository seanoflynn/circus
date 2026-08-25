using Circus.Actions;
using Circus.Events;
using Circus.MarketData;
using Circus.Matching;
using Circus.Restrictions;

namespace Circus;

public class OrderBook : IOrderBook
{
    private readonly Instrument _instrument;

    private OrderBookStatus _status = OrderBookStatus.Closed;

    private OrderBookStatusChangeReason _statusReason = OrderBookStatusChangeReason.Requested;

    private DateTime _lastActionTime;
    private long _nextSequenceNumber;

    private long _nextTradeId;
    private long? _lastTradedPrice;

    private DateTime? _resumeAt;
    private OrderBookStatus _resumeTo;

    private Side? _limitState;

    private (long PriceTicks, int Quantity)? _indicativeQuote;

    private readonly IReadOnlyList<IPriceRestriction> _priceRestrictions;

    private readonly Matcher _matcher = new();

    private readonly List<InternalOrder> _pendingImmediateOrCancelStops = new();

    public const int PublishedDepth = 10;

    private readonly DisplayedBookReport _report;

    private readonly IReadOnlyDictionary<OrderBookStatus, TradingPhase> _phases;

    private static IReadOnlyDictionary<OrderBookStatus, TradingPhase> BuildPhases(
        MatchingAlgorithm algorithm) =>
        new Dictionary<OrderBookStatus, TradingPhase>
        {
            {
                OrderBookStatus.PreOpen,
                new TradingPhase(new AuctionMatchingAlgorithm(), AcceptsOrderActions: true, AcceptsMarketOrders: false,
                    MatchesContinuously: false, StartsSession: true, ExpiresDayOrders: false)
            },
            {
                OrderBookStatus.Open,
                new TradingPhase(Continuous(algorithm), AcceptsOrderActions: true, AcceptsMarketOrders: true,
                    MatchesContinuously: true, StartsSession: false, ExpiresDayOrders: false)
            },
            {
                OrderBookStatus.Closed,
                new TradingPhase(null, AcceptsOrderActions: false, AcceptsMarketOrders: false,
                    MatchesContinuously: false, StartsSession: false, ExpiresDayOrders: true)
            },
            {
                OrderBookStatus.Paused,
                new TradingPhase(new AuctionMatchingAlgorithm(), AcceptsOrderActions: true, AcceptsMarketOrders: false,
                    MatchesContinuously: false, StartsSession: false, ExpiresDayOrders: false)
            },
            {
                OrderBookStatus.Halted,
                new TradingPhase(null, AcceptsOrderActions: true, AcceptsMarketOrders: false,
                    MatchesContinuously: false, StartsSession: false, ExpiresDayOrders: false)
            }
        };

    private static IMatchingAlgorithm Continuous(MatchingAlgorithm algorithm) => algorithm switch
    {
        MatchingAlgorithm.PriceTime => new PriceTimeMatchingAlgorithm(),
        MatchingAlgorithm.ProRata => new ProRataMatchingAlgorithm(),
        _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm,
            "an instrument allocates its continuous trading under price-time or pro-rata")
    };

    private TradingPhase CurrentPhase => _phases[_status];

    // Keyed by InternalId, not ExchangeOrderId - the latter changes across an order's life.
    private readonly Dictionary<long, InternalOrder> _orders = new();

    private DateOnly? _tradeDate;

    // Entries are never removed: a (companyId, clientOrderId) pair stays reserved for the life of
    // the book, which is what OrderIdAlreadyUsed is checked against.
    private readonly Dictionary<(string CompanyId, string ClientOrderId), InternalOrder> _clientOrderIndex = new();

    private const int MaxClientOrderIdLength = 20;

    public OrderBook(Instrument instrument)
        : this(instrument, Adapt(instrument.PriceRestrictions))
    {
    }

    private static IReadOnlyList<IPriceRestriction> Adapt(IReadOnlyList<PriceRestriction>? configs) =>
        configs == null
            ? Array.Empty<IPriceRestriction>()
            : configs.Select<PriceRestriction, IPriceRestriction>(config => config switch
            {
                OrderPriceBand band => new OrderPriceBandRestriction(band.BandTicks),
                VolatilityBand band => new VolatilityBandRestriction(band.RangeTicks, band.PauseFor,
                    band.Window, band.ExtendedRangeTicks),
                StaticPriceRange range => new StaticPriceRangeRestriction(range.RangeTicks, range.PauseFor),

                VelocityLimit limit => new VolatilityBandRestriction(limit.RangeTicks, limit.PauseFor,
                    limit.Window),
                DailyPriceLimit limit => new DailyPriceLimitRestriction(limit.Width),
                CircuitBreaker breaker => new CircuitBreakerRestriction(breaker.Width, breaker.HaltFor),
                _ => throw new ArgumentException($"Unknown price restriction {config.GetType().Name}")
            }).ToList();

    internal OrderBook(Instrument instrument, IReadOnlyList<IPriceRestriction> priceRestrictions)
    {
        _instrument = instrument;
        _priceRestrictions = priceRestrictions;
        _phases = BuildPhases(instrument.MatchingAlgorithm);

        _report = new DisplayedBookReport(instrument.Symbol, instrument.TickSize);
    }

    public string Symbol => _instrument.Symbol;
    public OrderBookStatus Status => _status;

    public IReadOnlyList<OrderBookEvent> Process(OrderBookAction action)
    {
        var time = action.Time;
        if (time == default)
            throw new ArgumentException(
                $"{action.GetType().Name} has no Time. Set it when constructing the action, or " +
                "drive the book through a TimestampingOrderBook to have a clock stamp it.",
                nameof(action));

        if (time < _lastActionTime)
            throw new ArgumentException(
                $"{action.GetType().Name} is stamped {time:O}, behind the previous action's " +
                $"{_lastActionTime:O}. Actions must arrive in time order.",
                nameof(action));

        _lastActionTime = time;

        _report.CaptureBefore(_matcher.Working[Side.Buy], _matcher.Working[Side.Sell]);

        var events = ResumeIfDue(time);
        events.AddRange(Handle(action, time));

        var quoteChange = TakeIndicativeQuoteChange(time);
        if (quoteChange != null)
            events.Add(quoteChange);

        _report.Append(events, time, _matcher.Working[Side.Buy], _matcher.Working[Side.Sell]);

        return events;
    }

    private List<OrderBookEvent> Handle(OrderBookAction action, DateTime time)
    {
        return action switch
        {
            CreateLimitOrder o => CreateOrder(o.CompanyId, o.ClientOrderId, o.OrderValidity, o.Side, o.Quantity,
                OrderType.Limit, o.Price, null, o.SelfMatchPrevention, o.MaxVisibleQuantity, time),
            CreateMarketOrder o => CreateOrder(o.CompanyId, o.ClientOrderId, o.OrderValidity, o.Side, o.Quantity,
                OrderType.Market, null, null, o.SelfMatchPrevention, o.MaxVisibleQuantity, time),
            CreateMarketLimitOrder o => CreateOrder(o.CompanyId, o.ClientOrderId, o.OrderValidity, o.Side,
                o.Quantity, OrderType.MarketLimit, null, null, o.SelfMatchPrevention, o.MaxVisibleQuantity, time),
            CreateStopLimitOrder o => CreateOrder(o.CompanyId, o.ClientOrderId, o.OrderValidity, o.Side,
                o.Quantity, OrderType.StopLimit, o.Price, o.TriggerPrice, o.SelfMatchPrevention, o.MaxVisibleQuantity, time),
            CreateStopMarketOrder o => CreateOrder(o.CompanyId, o.ClientOrderId, o.OrderValidity, o.Side,
                o.Quantity, OrderType.StopMarket, null, o.TriggerPrice, o.SelfMatchPrevention, o.MaxVisibleQuantity, time),
            UpdateOrder update => UpdateOrder(update.CompanyId, update.ClientOrderId,
                update.PreviousClientOrderId, update.NewTotalQuantity, update.Price, update.TriggerPrice, time),
            CancelOrder cancel => CancelOrder(cancel.CompanyId, cancel.ClientOrderId, cancel.PreviousClientOrderId, time),
            PreOpenTrading s => UpdateStatus(OrderBookStatus.PreOpen, s.ReferencePrice, true, OrderBookStatusChangeReason.Requested, time, s.TradeDate),
            OpenTrading s => UpdateStatus(OrderBookStatus.Open, s.ReferencePrice, true, OrderBookStatusChangeReason.Requested, time, s.TradeDate),
            CloseTrading c => UpdateStatus(OrderBookStatus.Closed, null, c.EndsTradingDay, OrderBookStatusChangeReason.Requested, time, c.TradeDate),
            PauseTrading => UpdateStatus(OrderBookStatus.Paused, null, true, OrderBookStatusChangeReason.Requested, time),
            HaltTrading => UpdateStatus(OrderBookStatus.Halted, null, true, OrderBookStatusChangeReason.Requested, time),

            AdvanceTime => new List<OrderBookEvent>(),

            PublishSnapshot => new List<OrderBookEvent> {Snapshot(time)},
            _ => throw new ArgumentException("Unknown order book action")
        };
    }

    private List<OrderBookEvent> ResumeIfDue(DateTime time)
    {
        if (_resumeAt == null || time < _resumeAt.Value)
            return new List<OrderBookEvent>();

        var due = _resumeAt.Value;
        _resumeAt = null;
        return UpdateStatus(_resumeTo, null, true, OrderBookStatusChangeReason.InterruptionElapsed, due);
    }

    private OrderBookEvent? TakeIndicativeQuoteChange(DateTime time)
    {
        var algorithm = CurrentPhase.Algorithm;

        (long PriceTicks, int Quantity)? quote = null;
        if (algorithm != null &&
            algorithm.TryQuoteIndicative(_matcher.Working, out var priceTicks, out var quantity))
            quote = (priceTicks, quantity);

        if (Nullable.Equals(quote, _indicativeQuote))
            return null;

        _indicativeQuote = quote;

        foreach (var restriction in _priceRestrictions)
            restriction.OnIndicativePrice(quote?.PriceTicks);

        return new IndicativePriceChanged(_instrument.Symbol, time,
            quote.HasValue ? (decimal?) ToDecimal(quote.Value.PriceTicks) : null, quote?.Quantity ?? 0);
    }

    private List<OrderBookEvent> CreateOrder(string companyId, string clientOrderId, OrderValidity validity,
        Side side, int quantity, OrderType type, decimal? price = null, decimal? triggerPrice = null,
        SelfMatchPrevention? selfMatchPrevention = null, int? maxVisibleQuantity = null, DateTime time = default)
    {
        var selfMatchPreventionId = selfMatchPrevention?.Id;
        var selfMatchPreventionInstruction = selfMatchPrevention?.Instruction;
        var status = triggerPrice.HasValue ? OrderStatus.Hidden : OrderStatus.Working;

        if (!CurrentPhase.AcceptsOrderActions)
            return RejectCreate(companyId, clientOrderId, OrderRejectedReason.MarketClosed, time);
        if (type == OrderType.Market && !CurrentPhase.AcceptsMarketOrders)
            return RejectCreate(companyId, clientOrderId, OrderRejectedReason.MarketOrdersNotAccepted, time);
        if (string.IsNullOrEmpty(clientOrderId))
            return RejectCreate(companyId, clientOrderId, OrderRejectedReason.ClientOrderIdRequired, time);
        if (clientOrderId.Length > MaxClientOrderIdLength)
            return RejectCreate(companyId, clientOrderId, OrderRejectedReason.ClientOrderIdTooLong, time);
        if (string.IsNullOrEmpty(companyId))
            return RejectCreate(companyId, clientOrderId, OrderRejectedReason.CompanyIdRequired, time);
        if (companyId.Length > MaxClientOrderIdLength)
            return RejectCreate(companyId, clientOrderId, OrderRejectedReason.CompanyIdTooLong, time);
        if (selfMatchPreventionId != null && selfMatchPreventionId.Length > MaxClientOrderIdLength)
            return RejectCreate(companyId, clientOrderId, OrderRejectedReason.SelfMatchPreventionIdTooLong, time);
        if (quantity < 1)
            return RejectCreate(companyId, clientOrderId, OrderRejectedReason.InvalidQuantity, time);
        if (!TryConvertToTicks(price, out var priceTicks))
            return RejectCreate(companyId, clientOrderId, OrderRejectedReason.InvalidPriceIncrement, time);
        if (!TryConvertToTicks(triggerPrice, out var triggerTicks))
            return RejectCreate(companyId, clientOrderId, OrderRejectedReason.InvalidPriceIncrement, time);
        if (triggerTicks != null && priceTicks != null && side == Side.Buy && priceTicks < triggerTicks)
            return RejectCreate(companyId, clientOrderId, OrderRejectedReason.TriggerPriceMustBeLessThanPrice, time);
        if (triggerTicks != null && priceTicks != null && side == Side.Sell && priceTicks > triggerTicks)
            return RejectCreate(companyId, clientOrderId, OrderRejectedReason.TriggerPriceMustBeGreaterThanPrice, time);
        if (triggerTicks != null && priceTicks != null &&
            !AllowsStopSpread(triggerTicks.Value, priceTicks.Value))
            return RejectCreate(companyId, clientOrderId, OrderRejectedReason.TriggerPriceTooFarFromPrice, time);
        if (triggerTicks != null && !_lastTradedPrice.HasValue)
            return RejectCreate(companyId, clientOrderId, OrderRejectedReason.NoLastTradedPrice, time);
        if (triggerTicks != null && side == Side.Buy && triggerTicks <= _lastTradedPrice)
            return RejectCreate(companyId, clientOrderId, OrderRejectedReason.TriggerPriceMustBeGreaterThanLastTradedPrice, time);
        if (triggerTicks != null && side == Side.Sell && triggerTicks >= _lastTradedPrice)
            return RejectCreate(companyId, clientOrderId, OrderRejectedReason.TriggerPriceMustBeLessThanLastTradedPrice, time);
        if (priceTicks.HasValue && FindOrderEntryRefusal(priceTicks.Value, time) is { } entryRefusal)
            return RejectCreate(companyId, clientOrderId, entryRefusal, time);
        if (validity is OrderValidity.ImmediateOrCancel { MinQuantity: int minQty } && (minQty < 1 || minQty > quantity))
            return RejectCreate(companyId, clientOrderId, OrderRejectedReason.MinQuantityOutOfRange, time);
        if (maxVisibleQuantity.HasValue && (maxVisibleQuantity < 1 || maxVisibleQuantity > quantity))
            return RejectCreate(companyId, clientOrderId, OrderRejectedReason.MaxVisibleQuantityOutOfRange, time);
        if (_clientOrderIndex.TryGetValue((companyId, clientOrderId), out var existingOrder))
        {
            return existingOrder.Status is OrderStatus.Working or OrderStatus.Hidden
                ? RejectCreate(companyId, clientOrderId, OrderRejectedReason.OrderInBook, time)
                : RejectCreate(companyId, clientOrderId, OrderRejectedReason.OrderIdAlreadyUsed, time);
        }

        if (type == OrderType.Market || type == OrderType.MarketLimit)
        {
            var protectionTicks = type == OrderType.MarketLimit ? 0 : _instrument.MarketOrderProtectionTicks;
            if(!TryGetLimitPrice(side, protectionTicks, out priceTicks))
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.NoOrdersToMatchMarketOrder, time);
        }

        if (validity is OrderValidity.ImmediateOrCancel { MinQuantity: int gateMinQty } && !triggerTicks.HasValue &&
            !_matcher.HasSufficientLiquidity(side, priceTicks!.Value, gateMinQty, selfMatchPreventionId,
                selfMatchPreventionInstruction))
            return RejectCreate(companyId, clientOrderId, OrderRejectedReason.InsufficientLiquidityForMinQuantity, time);
        if (validity is OrderValidity.GoodTilDate { Date: var goodTilDate } && goodTilDate < TradingDayOn(time))
            return RejectCreate(companyId, clientOrderId, OrderRejectedReason.InvalidExpireDate, time);

        _nextSequenceNumber++;
        var order = new InternalOrder(_nextSequenceNumber, companyId, clientOrderId, _instrument, time, status,
            type, validity, side, quantity, priceTicks, triggerTicks, selfMatchPreventionId,
            selfMatchPreventionInstruction, maxVisibleQuantity);

        _orders.Add(order.InternalId, order);
        _clientOrderIndex.Add((companyId, clientOrderId), order);
        _matcher.Rest(order);

        List<OrderBookEvent> events = new();
        events.Add(new CreateOrderConfirmed(_instrument.Symbol, time, companyId, order.ToOrder()));
        Match(events, time: time);

        if (order.Validity is OrderValidity.ImmediateOrCancel && order.Status == OrderStatus.Working)
            events.Add(CancelRemainder(order, OrderCancelledReason.ImmediateOrCancelNotFilled, time));

        return events;
    }

    private bool TryConvertToTicks(decimal? price, out long? ticks)
    {
        if (!price.HasValue)
        {
            ticks = null;
            return true;
        }

        var rawTicks = price.Value / _instrument.TickSize;
        var truncatedTicks = Math.Truncate(rawTicks);
        if (rawTicks != truncatedTicks)
        {
            ticks = null;
            return false;
        }

        ticks = (long) truncatedTicks;
        return true;
    }

    private decimal ToDecimal(long ticks) => ticks * _instrument.TickSize;

    private BookSnapshot Snapshot(DateTime time) =>
        new(_instrument.Symbol, time,
            GetLevels(Side.Buy, PublishedDepth), GetLevels(Side.Sell, PublishedDepth),
            GetRestingOrders(),
            _status, _statusReason, _resumeAt, _limitState,
            _indicativeQuote is { } quote ? ToDecimal(quote.PriceTicks) : null,
            _indicativeQuote?.Quantity ?? 0);

    internal IReadOnlyList<RestingOrder> GetRestingOrders()
    {
        var orders = new List<RestingOrder>();

        foreach (var side in new[] {Side.Buy, Side.Sell})
        {
            foreach (var (tick, first, _) in _matcher.Working[side].EnumerateFromBest())
            {
                for (var order = first; order != null; order = order.LevelNext)
                    orders.Add(new RestingOrder(side, order.ExchangeOrderId, ToDecimal(tick),
                        order.DisplayedQuantity));
            }
        }

        return orders;
    }

    internal IReadOnlyList<Level> GetLevels(Side side, int maxLevels)
    {
        if (maxLevels <= 0)
            return Array.Empty<Level>();

        var scratch = new List<(long Tick, int Quantity, int Count)>(maxLevels);
        _matcher.Working[side].CopyLevelsFromBest(maxLevels, scratch);

        var levels = new List<Level>(scratch.Count);
        foreach (var (tick, quantity, count) in scratch)
            levels.Add(new Level(ToDecimal(tick), quantity, count));

        return levels;
    }

    private bool TryGetLimitPrice(Side side, int protectionTicks, out long? priceTicks)
    {
        priceTicks = null;
        var opposing = _matcher.Working[side == Side.Buy ? Side.Sell : Side.Buy];
        if (!opposing.TryGetBest(out var bestTick, out _))
            return false;

        // set price as best offer + protection ticks for buy orders, best bid - protection ticks for sell orders
        // TODO: option to use best bid + protection tickets for buy orders, etc (eurex)
        priceTicks = bestTick + ((side == Side.Buy ? 1 : -1) * protectionTicks);
        return true;
    }

    private OrderRejectedReason? FindOrderEntryRefusal(long priceTicks, DateTime time)
    {
        foreach (var restriction in _priceRestrictions)
        {
            if (restriction.Scope.HasFlag(RestrictionScope.OrderEntry) &&
                !restriction.Allows(priceTicks, time))
                return restriction.EntryRejectionReason;
        }

        return null;
    }

    private bool AllowsStopSpread(long triggerTicks, long priceTicks) =>
        _priceRestrictions.Where(r => r.Scope.HasFlag(RestrictionScope.OrderEntry))
            .All(r => r.AllowsStopSpread(Math.Abs(priceTicks - triggerTicks)));

    private OrderBookEvent CancelRemainder(InternalOrder order, OrderCancelledReason reason, DateTime time)
    {
        var previousClientOrderId = order.ClientOrderId;
        var previousPrice = ToDecimal(order.Price!.Value);
        var previousQuantity = order.DisplayedQuantity;
        order.Cancel(time);
        CompleteOrder(order);
        return new CancelOrderConfirmed(_instrument.Symbol, time, order.CompanyId, order.ToOrder(), previousClientOrderId,
            reason, previousPrice, previousQuantity);
    }

    private List<OrderBookEvent> UpdateOrder(string companyId, string clientOrderId, string previousClientOrderId,
        int? newTotalQuantity = null, decimal? price = null, decimal? triggerPrice = null, DateTime time = default)
    {
        if (!CurrentPhase.AcceptsOrderActions)
            return RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.MarketClosed, time: time);
        if (string.IsNullOrEmpty(clientOrderId))
            return RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.ClientOrderIdRequired, time: time);
        if (clientOrderId.Length > MaxClientOrderIdLength)
            return RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.ClientOrderIdTooLong, time: time);
        if (string.IsNullOrEmpty(companyId))
            return RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.CompanyIdRequired, time: time);
        if (companyId.Length > MaxClientOrderIdLength)
            return RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.CompanyIdTooLong, time: time);
        if (newTotalQuantity == null && price == null && triggerPrice == null)
            return RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.NoChange, time: time);
        if (newTotalQuantity != null && newTotalQuantity < 1)
            return RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.InvalidQuantity, time: time);
        if (!TryConvertToTicks(price, out var priceTicks))
            return RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.InvalidPriceIncrement, time: time);
        if (!TryConvertToTicks(triggerPrice, out var triggerTicks))
            return RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.InvalidPriceIncrement, time: time);
        if (priceTicks.HasValue && FindOrderEntryRefusal(priceTicks.Value, time) is { } entryRefusal)
            return RejectUpdate(companyId, clientOrderId, previousClientOrderId, entryRefusal, time: time);
        if (!_clientOrderIndex.TryGetValue((companyId, previousClientOrderId), out var order) ||
            order.ClientOrderId != previousClientOrderId)
            return RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.OrderNotInBook, time: time);
        if (order.Status is not (OrderStatus.Working or OrderStatus.Hidden))
            return RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.TooLateToCancel,
                order.ExchangeOrderId, time);
        if (_clientOrderIndex.TryGetValue((companyId, clientOrderId), out var conflictingOrder))
        {
            return conflictingOrder.Status is OrderStatus.Working or OrderStatus.Hidden
                ? RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.OrderInBook,
                    order.ExchangeOrderId, time)
                : RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.OrderIdAlreadyUsed,
                    order.ExchangeOrderId, time);
        }

        if (order.Status == OrderStatus.Hidden)
        {
            var newTriggerTicks = triggerTicks ?? order.TriggerPrice;
            var newPriceTicks = priceTicks ?? order.Price;

            if (newTriggerTicks != null && newPriceTicks != null && order.Side == Side.Buy && newPriceTicks < newTriggerTicks)
                return RejectUpdate(companyId, clientOrderId, previousClientOrderId,
                    OrderRejectedReason.TriggerPriceMustBeLessThanPrice, order.ExchangeOrderId, time);
            if (newTriggerTicks != null && newPriceTicks != null && order.Side == Side.Sell && newPriceTicks > newTriggerTicks)
                return RejectUpdate(companyId, clientOrderId, previousClientOrderId,
                    OrderRejectedReason.TriggerPriceMustBeGreaterThanPrice, order.ExchangeOrderId, time);
            if (newTriggerTicks != null && newPriceTicks != null &&
                !AllowsStopSpread(newTriggerTicks.Value, newPriceTicks.Value))
                return RejectUpdate(companyId, clientOrderId, previousClientOrderId,
                    OrderRejectedReason.TriggerPriceTooFarFromPrice, order.ExchangeOrderId, time);

            if (triggerTicks != null && order.Side == Side.Buy && triggerTicks <= _lastTradedPrice)
                return RejectUpdate(companyId, clientOrderId, previousClientOrderId,
                    OrderRejectedReason.TriggerPriceMustBeGreaterThanLastTradedPrice, order.ExchangeOrderId, time);
            if (triggerTicks != null && order.Side == Side.Sell && triggerTicks >= _lastTradedPrice)
                return RejectUpdate(companyId, clientOrderId, previousClientOrderId,
                    OrderRejectedReason.TriggerPriceMustBeLessThanLastTradedPrice, order.ExchangeOrderId, time);
        }
        else
        {
            triggerTicks = null;
        }

        // TODO: can't update price on stop market order?

        var previousQuantity = order.DisplayedQuantity;
        var previousPrice = order.Status == OrderStatus.Hidden ? (decimal?) null : ToDecimal(order.Price!.Value);

        if (newTotalQuantity <= order.FilledQuantity)
        {
            order.Cancel(time, clientOrderId);
            _clientOrderIndex[(companyId, clientOrderId)] = order;
            CompleteOrder(order);

            return new List<OrderBookEvent>
            {
                new CancelOrderConfirmed(_instrument.Symbol, time, order.CompanyId, order.ToOrder(), previousClientOrderId,
                    OrderCancelledReason.UpdatedQuantityLowerThanFilledQuantity, previousPrice, previousQuantity)
            };
        }

        var sequenceNumber = order.SequenceNumber;
        var isPriceChange = (triggerTicks != null && order.Status == OrderStatus.Hidden && triggerTicks != order.TriggerPrice) ||
                            (priceTicks != null && order.Status != OrderStatus.Hidden && priceTicks != order.Price);
        var isQuantityIncrease = order.MaxVisibleQuantity == null &&
            (newTotalQuantity != null && newTotalQuantity > order.Quantity);

        if (isPriceChange || isQuantityIncrease)
        {
            _nextSequenceNumber++;
            sequenceNumber = _nextSequenceNumber;
            var updatedPriceTicks =
                (order.Status == OrderStatus.Hidden ? triggerTicks ?? order.TriggerPrice : priceTicks ?? order.Price) ??
                throw new InvalidOperationException("missing price");

            _matcher.Reprice(order, updatedPriceTicks);
        }

        var previousExchangeOrderId = order.ExchangeOrderId;
        order.Update(sequenceNumber, time, newTotalQuantity, triggerTicks, priceTicks, clientOrderId);

        _matcher.SyncDisplayed(order, previousQuantity);
        _clientOrderIndex[(companyId, clientOrderId)] = order;

        List<OrderBookEvent> events = new();
        events.Add(new UpdateOrderConfirmed(_instrument.Symbol, time, order.CompanyId, order.ToOrder(), previousClientOrderId,
            previousExchangeOrderId, previousPrice, previousQuantity));
        Match(events, time: time);
        return events;
    }

    private List<OrderBookEvent> CancelOrder(string companyId, string clientOrderId, string previousClientOrderId, DateTime time)
    {
        if (!CurrentPhase.AcceptsOrderActions)
            return RejectCancel(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.MarketClosed, time: time);
        if (string.IsNullOrEmpty(clientOrderId))
            return RejectCancel(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.ClientOrderIdRequired, time: time);
        if (clientOrderId.Length > MaxClientOrderIdLength)
            return RejectCancel(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.ClientOrderIdTooLong, time: time);
        if (string.IsNullOrEmpty(companyId))
            return RejectCancel(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.CompanyIdRequired, time: time);
        if (companyId.Length > MaxClientOrderIdLength)
            return RejectCancel(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.CompanyIdTooLong, time: time);
        if (!_clientOrderIndex.TryGetValue((companyId, previousClientOrderId), out var order) ||
            order.ClientOrderId != previousClientOrderId)
            return RejectCancel(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.OrderNotInBook, time: time);
        if (order.Status is not (OrderStatus.Working or OrderStatus.Hidden))
            return RejectCancel(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.TooLateToCancel,
                order.ExchangeOrderId, time);
        if (_clientOrderIndex.TryGetValue((companyId, clientOrderId), out var conflictingOrder))
        {
            return conflictingOrder.Status is OrderStatus.Working or OrderStatus.Hidden
                ? RejectCancel(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.OrderInBook,
                    order.ExchangeOrderId, time)
                : RejectCancel(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.OrderIdAlreadyUsed,
                    order.ExchangeOrderId, time);
        }

        var previousPrice = order.Status == OrderStatus.Hidden ? (decimal?) null : ToDecimal(order.Price!.Value);
        var previousQuantity = order.DisplayedQuantity;
        order.Cancel(time, clientOrderId);
        _clientOrderIndex[(companyId, clientOrderId)] = order;
        CompleteOrder(order);

        return new List<OrderBookEvent>
        {
            new CancelOrderConfirmed(_instrument.Symbol, time, order.CompanyId, order.ToOrder(), previousClientOrderId,
                OrderCancelledReason.Cancelled, previousPrice, previousQuantity)
        };
    }

    private List<OrderBookEvent> RejectCreate(string companyId, string clientOrderId, OrderRejectedReason reason, DateTime time) =>
        new() {new CreateOrderRejected(_instrument.Symbol, time, companyId, clientOrderId, reason)};

    private List<OrderBookEvent> RejectUpdate(string companyId, string clientOrderId, string previousClientOrderId,
            OrderRejectedReason reason, string? exchangeOrderId = null, DateTime time = default) =>
        new()
        {
            new UpdateOrderRejected(_instrument.Symbol, time, companyId, clientOrderId, previousClientOrderId,
                exchangeOrderId, reason)
        };

    private List<OrderBookEvent> RejectCancel(string companyId, string clientOrderId, string previousClientOrderId,
            OrderRejectedReason reason, string? exchangeOrderId = null, DateTime time = default) =>
        new()
        {
            new CancelOrderRejected(_instrument.Symbol, time, companyId, clientOrderId, previousClientOrderId,
                exchangeOrderId, reason)
        };

    private OrderBookEvent ExpireOrder(InternalOrder order, DateTime time)
    {
        var previousPrice = order.Status == OrderStatus.Hidden ? (decimal?) null : ToDecimal(order.Price!.Value);
        var previousQuantity = order.DisplayedQuantity;
        order.Expire(time);
        CompleteOrder(order);

        return new ExpireOrderConfirmed(_instrument.Symbol, time, order.CompanyId, order.ToOrder(), previousPrice,
            previousQuantity);
    }

    private void CompleteOrder(InternalOrder order)
    {
        _matcher.Unrest(order);
        FinishOrder(order);
    }

    private void FinishOrder(InternalOrder order)
    {
        _orders.Remove(order.InternalId);
    }

    // Call immediately after order.Fill(...), having already snapshotted the order for
    // FillOrderConfirmed: a replenish changes ExchangeOrderId, and the fill was against the old one.
    private OrderBookEvent? FinishFill(InternalOrder order, DateTime time)
    {
        if (order.Status == OrderStatus.Filled)
        {
            CompleteOrder(order);
            return null;
        }

        if (order.DisplayedQuantity == 0 && order.MaxVisibleQuantity.HasValue)
        {
            var previousExchangeOrderId = order.ExchangeOrderId;
            var priceTicks = order.Price ?? throw new InvalidOperationException("limit order missing price");
            _matcher.Unrest(order);
            _nextSequenceNumber++;
            order.Replenish(_nextSequenceNumber, time);
            _matcher.Rest(order);

            return new UpdateOrderConfirmed(_instrument.Symbol, time, order.CompanyId, order.ToOrder(),
                order.ClientOrderId, previousExchangeOrderId, ToDecimal(priceTicks), 0);
        }

        return null;
    }

    private void Match(List<OrderBookEvent> events, IMatchingAlgorithm? algorithm = null, DateTime time = default)
    {
        var phase = CurrentPhase;
        if (!phase.MatchesContinuously)
        {
            return;
        }

        var continuous = phase.Algorithm ??
            throw new InvalidOperationException("a phase that matches continuously needs an algorithm");
        _pendingImmediateOrCancelStops.Clear();

        foreach (var outcome in _matcher.Run(algorithm ?? continuous, continuous,
                     priceTicks => CheckTradeRestrictionBreach(priceTicks, time)))
            Apply(outcome, events, time, _pendingImmediateOrCancelStops);

        foreach (var order in _pendingImmediateOrCancelStops)
        {
            if (order.RemainingQuantity > 0)
                events.Add(CancelRemainder(order, OrderCancelledReason.ImmediateOrCancelNotFilled, time));
        }
    }

    private RestrictionBreach? CheckTradeRestrictionBreach(long priceTicks, DateTime time)
    {
        RestrictionBreach? worst = null;

        foreach (var restriction in _priceRestrictions)
        {
            if (!restriction.Scope.HasFlag(RestrictionScope.Trade) || restriction.Allows(priceTicks, time))
                continue;

            var breach = new RestrictionBreach(restriction.OnBreach, restriction.ResumeAfter);
            if (worst == null || IsMoreSevere(breach, worst.Value))
                worst = breach;
        }

        return worst;
    }

    private static bool IsMoreSevere(RestrictionBreach candidate, RestrictionBreach current)
    {
        if (Severity(candidate.Action) != Severity(current.Action))
            return Severity(candidate.Action) > Severity(current.Action);

        if (!candidate.ResumeAfter.HasValue || !current.ResumeAfter.HasValue)
            return !candidate.ResumeAfter.HasValue && current.ResumeAfter.HasValue;

        return candidate.ResumeAfter.Value > current.ResumeAfter.Value;
    }

    private RestrictionBreach? CheckResumptionRefusal(OrderBookStatus arrivingStatus, DateTime time)
    {
        var departing = CurrentPhase;
        if (!departing.PrintsOnExit || !_phases[arrivingStatus].MatchesContinuously)
            return null;

        if (!departing.Algorithm!.TryQuoteIndicative(_matcher.Working, out var priceTicks, out _))
            return null;

        foreach (var restriction in _priceRestrictions)
        {
            if (restriction.Scope.HasFlag(RestrictionScope.Trade) &&
                !restriction.AllowsResumption(priceTicks, time))
                return new RestrictionBreach(restriction.OnBreach, restriction.ResumeAfter);
        }

        return null;
    }

    private static int Severity(RestrictionBreachAction action) => action switch
    {
        RestrictionBreachAction.Halt => 3,
        RestrictionBreachAction.Pause => 2,

        RestrictionBreachAction.Block => 1,
        _ => 0
    };

    private void Apply(MatchOutcome outcome, List<OrderBookEvent> events, DateTime time,
        List<InternalOrder> pendingImmediateOrCancelStops)
    {
        switch (outcome)
        {
            case SelfMatchDetected(var resting, var aggressor, var instruction):
                if (instruction != SelfMatchPreventionInstruction.CancelAggressor)
                    events.Add(CancelRemainder(resting, OrderCancelledReason.SelfMatchPrevention, time));
                if (instruction != SelfMatchPreventionInstruction.CancelResting)
                    events.Add(CancelRemainder(aggressor, OrderCancelledReason.SelfMatchPrevention, time));
                break;

            case TradeExecuted(var resting, var aggressor, var priceTicks, var quantity, var usesFullRemainingQuantity):
                ApplyTrade(resting, aggressor, priceTicks, quantity, usesFullRemainingQuantity, events, time);
                break;

            case TradeRestrictionBreached(var blockedTicks, {Action: RestrictionBreachAction.Block}):
                TakeLimitStateChange(blockedTicks, events, time);
                break;

            // Assigned directly rather than routed through UpdateStatus: this runs inside the Run/Apply
            // loop, and UpdateStatus matches, which would re-enter the matcher mid-enumeration.
            case TradeRestrictionBreached(_, var breach):
                _resumeTo = _status;
                _status = breach.Action == RestrictionBreachAction.Halt
                    ? OrderBookStatus.Halted
                    : OrderBookStatus.Paused;
                _resumeAt = breach.ResumeAfter.HasValue ? time + breach.ResumeAfter.Value : null;
                _statusReason = OrderBookStatusChangeReason.PriceRestriction;
                events.Add(new StatusChanged(_instrument.Symbol, time, _status, _statusReason,
                    _resumeAt, _limitState));
                break;

            case StopsTriggered(var orders):
                TriggerStops(orders, time, events, pendingImmediateOrCancelStops);
                break;
        }
    }

    private void TakeLimitStateChange(long blockedTicks, List<OrderBookEvent> events, DateTime time)
    {
        var side = _lastTradedPrice switch
        {
            null => (Side?) null,
            var last when blockedTicks > last => Side.Buy,
            var last when blockedTicks < last => Side.Sell,
            _ => _limitState
        };

        if (_limitState == side)
            return;

        _limitState = side;
        events.Add(new LimitStateChanged(_instrument.Symbol, time, side, ToDecimal(blockedTicks),
            _status, _statusReason, _resumeAt));
    }

    private void ReleaseLimitState(List<OrderBookEvent> events, DateTime time)
    {
        if (_limitState == null)
            return;

        _limitState = null;
        events.Add(new LimitStateChanged(_instrument.Symbol, time, null, null,
            _status, _statusReason, _resumeAt));
    }

    private void ApplyTrade(InternalOrder resting, InternalOrder aggressor, long priceTicks, int quantity,
        bool usesFullRemainingQuantity, List<OrderBookEvent> events, DateTime time)
    {
        var price = ToDecimal(priceTicks);

        void FillOrder(InternalOrder order)
        {
            if (usesFullRemainingQuantity)
                order.FillFullSize(time, quantity);
            else
                order.Fill(time, quantity);
        }

        var restingDisplayed = resting.DisplayedQuantity;
        FillOrder(resting);
        _matcher.SyncDisplayed(resting, restingDisplayed);
        var restingSnapshot = resting.ToOrder();
        var restingReplenish = FinishFill(resting, time);

        var aggressorDisplayed = aggressor.DisplayedQuantity;
        FillOrder(aggressor);
        _matcher.SyncDisplayed(aggressor, aggressorDisplayed);
        var aggressorSnapshot = aggressor.ToOrder();
        var aggressorReplenish = FinishFill(aggressor, time);

        _nextTradeId++;
        var tradeId = _nextTradeId.ToString();

        events.Add(new FillOrderConfirmed(_instrument.Symbol, time, resting.CompanyId, restingSnapshot, tradeId,
            price, quantity, restingDisplayed, true));
        events.Add(new FillOrderConfirmed(_instrument.Symbol, time, aggressor.CompanyId, aggressorSnapshot, tradeId,
            price, quantity, aggressorDisplayed, false));

        if (restingReplenish != null)
            events.Add(restingReplenish);
        if (aggressorReplenish != null)
            events.Add(aggressorReplenish);

        ReleaseLimitState(events, time);

        if (_lastTradedPrice != priceTicks)
        {
            _lastTradedPrice = priceTicks;
            foreach (var phase in _phases.Values)
                phase.Algorithm?.OnTrade(priceTicks);
            foreach (var restriction in _priceRestrictions)
                restriction.OnTrade(priceTicks, time);
        }
    }

    private void TriggerStops(IReadOnlyList<InternalOrder> orders, DateTime time, List<OrderBookEvent> events,
        List<InternalOrder> pendingImmediateOrCancelStops)
    {
        foreach (var order in orders)
        {
            _matcher.Unrest(order);

            if (order.Validity is OrderValidity.ImmediateOrCancel)
                pendingImmediateOrCancelStops.Add(order);

            long? newPriceTicks = order.Price;
            if (order.Type == OrderType.StopMarket &&
                !TryGetLimitPrice(order.Side, _instrument.MarketOrderProtectionTicks, out newPriceTicks))
            {
                var previousClientOrderId = order.ClientOrderId;
                var previousQuantity = order.DisplayedQuantity;
                order.Cancel(time);
                FinishOrder(order);

                events.Add(new CancelOrderConfirmed(_instrument.Symbol, time, order.CompanyId, order.ToOrder(),
                    previousClientOrderId, OrderCancelledReason.NoOrdersToMatchMarketOrder, null,
                    previousQuantity));
                continue;
            }

            if (order.Validity is OrderValidity.ImmediateOrCancel { MinQuantity: int stopMinQty } &&
                !_matcher.HasSufficientLiquidity(order.Side, newPriceTicks!.Value, stopMinQty,
                    order.SelfMatchPreventionId, order.SelfMatchPreventionInstruction))
            {
                var previousClientOrderId = order.ClientOrderId;
                var previousQuantity = order.DisplayedQuantity;
                order.Cancel(time);
                FinishOrder(order);

                events.Add(new CancelOrderConfirmed(_instrument.Symbol, time, order.CompanyId, order.ToOrder(),
                    previousClientOrderId, OrderCancelledReason.ImmediateOrCancelNotFilled, null,
                    previousQuantity));
                continue;
            }

            var previousExchangeOrderId = order.ExchangeOrderId;
            _nextSequenceNumber++;
            order.ConvertToLimit(time, _nextSequenceNumber, newPriceTicks);

            _matcher.Rest(order);

            events.Add(new UpdateOrderConfirmed(_instrument.Symbol, time, order.CompanyId, order.ToOrder(),
                order.ClientOrderId, previousExchangeOrderId, null, order.DisplayedQuantity));
        }
    }

    private List<OrderBookEvent> UpdateStatus(OrderBookStatus status, decimal? referencePrice = null,
        bool endsTradingDay = true, OrderBookStatusChangeReason reason = OrderBookStatusChangeReason.Requested,
        DateTime time = default, DateOnly? tradeDate = null)
    {
        if (tradeDate.HasValue) _tradeDate = tradeDate;

        if (referencePrice.HasValue && TryConvertToTicks(referencePrice, out var referenceTicks))
        {
            foreach (var phase in _phases.Values)
                phase.Algorithm?.OnSessionChange(referenceTicks);
            foreach (var restriction in _priceRestrictions)
                restriction.OnSessionChange(referenceTicks);
        }

        if (!_phases.ContainsKey(status))
            throw new ArgumentOutOfRangeException(nameof(status), status, "no phase configured for this status");

        var extension = CheckResumptionRefusal(status, time);
        if (extension != null)
        {
            _resumeAt = extension.Value.ResumeAfter.HasValue ? time + extension.Value.ResumeAfter.Value : null;
            _statusReason = OrderBookStatusChangeReason.PriceRestriction;
            return new List<OrderBookEvent>
                {new StatusChanged(_instrument.Symbol, time, _status, _statusReason, _resumeAt, _limitState)};
        }

        _resumeAt = null;

        var departing = CurrentPhase;
        _status = status;
        var arriving = CurrentPhase;

        if (arriving.StartsSession)
        {
            var day = TradingDayOn(time);
            var seed = ((day.Year * 10000) + (day.Month * 100) + day.Day) * 10000000000L;
            // Forward-only. Restarting the counter would re-issue ids that orders surviving the previous
            // session (GTC, or GTD not yet due) still hold, and _orders is keyed on exactly that.
            _nextSequenceNumber = Math.Max(_nextSequenceNumber, seed);

            _nextTradeId = Math.Max(_nextTradeId, seed);
        }

        _statusReason = reason;
        var events = new List<OrderBookEvent>
            {new StatusChanged(_instrument.Symbol, time, _status, reason, _resumeAt, _limitState)};

        if (departing.PrintsOnExit)
            Match(events, departing.Algorithm, time);

        Match(events, time: time);

        if (arriving.ExpiresDayOrders && endsTradingDay)
            events.AddRange(ExpireOrders(time));

        return events;
    }

    private DateOnly TradingDayOn(DateTime time) => _tradeDate ?? DateOnly.FromDateTime(time);

    private IEnumerable<OrderBookEvent> ExpireOrders(DateTime time)
    {
        var today = TradingDayOn(time);
        var orders = _orders.Values.Where(o =>
            o.Validity is OrderValidity.Day ||
            (o.Validity is OrderValidity.GoodTilDate { Date: var date } && date <= today))
            .OrderBy(o => o.InternalId)
            .ToList();

        return orders.Select(o => ExpireOrder(o, time)).ToList();
    }
}
