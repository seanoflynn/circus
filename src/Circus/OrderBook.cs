using Circus.Actions;
using Circus.Events;
using Circus.Matching;
using Circus.Restrictions;

namespace Circus;

// Working state, not a record of it. A book holds the day's orders for as long as the process
// does and writes nothing down, which is what a book is rather than a limitation of this one:
// durability belongs to whatever journals the action stream on the way in, and rebuilds a book
// by replaying it rather than by asking this one what it holds.
//
// A pure function of the actions it is given: it reads no clock and consults nothing ambient,
// so the same actions always produce the same events. Time arrives stamped on each action -
// see TimestampingOrderBook for the boundary that does the stamping.
public class OrderBook : IOrderBook
{
    private readonly Instrument _instrument;

    private OrderBookStatus _status = OrderBookStatus.Closed;

    // The last action's stamp, kept only to refuse one that moves time backwards. Set from the
    // first action, so a book has no opinion about what time it is until something tells it.
    private DateTime _lastActionTime;
    private long _nextSequenceNumber;
    private long? _lastTradedPrice;

    // A timed interruption's deadline and where it returns to. Null whenever the book is not
    // serving one, which includes an interruption configured to last until told otherwise.
    private DateTime? _resumeAt;
    private OrderBookStatus _resumeTo;

    // Which way a daily limit currently has the market stuck, so the state is published on the
    // change rather than on every sweep it turns away. Null is a market free to trade.
    private Side? _limitState;

    // The indicative quote as last published, so only moves in it are emitted.
    private (long PriceTicks, int Quantity)? _indicativeQuote;

    // Order-entry and trade-time price bands, each maintaining its own reference anchor.
    // A future velocity limit or circuit breaker is a new entry here, not a redesign.
    private readonly IReadOnlyList<IPriceRestriction> _priceRestrictions;

    // Owns the working/stop ladders and the pure decision helpers (liquidity checks,
    // self-match verdicts) that read them.
    private readonly Matcher _matcher = new();

    // Nothing outside this table names a status - the rest of the book reads the current phase
    // and acts on what it says. Pre-open's auction instance is also what prints on the way out
    // of it, so the opening print is the quote it had been publishing.
    private readonly IReadOnlyDictionary<OrderBookStatus, TradingPhase> _phases =
        new Dictionary<OrderBookStatus, TradingPhase>
        {
            {
                OrderBookStatus.PreOpen,
                new TradingPhase(new AuctionMatchingAlgorithm(), AcceptsOrderActions: true, AcceptsMarketOrders: false,
                    MatchesContinuously: false, StartsSession: true, ExpiresDayOrders: false)
            },
            {
                OrderBookStatus.Open,
                new TradingPhase(new PriceTimeMatchingAlgorithm(), AcceptsOrderActions: true, AcceptsMarketOrders: true,
                    MatchesContinuously: true, StartsSession: false, ExpiresDayOrders: false)
            },
            {
                OrderBookStatus.Closed,
                new TradingPhase(null, AcceptsOrderActions: false, AcceptsMarketOrders: false,
                    MatchesContinuously: false, StartsSession: false, ExpiresDayOrders: true)
            },
            {
                // Quotes and prints on the way out exactly as pre-open does, so a pause
                // resolves into a single uncrossing price rather than resuming mid-sweep. It is
                // an interruption within a session and not the start of one, though, so unlike
                // pre-open it neither reseeds sequence numbers nor retires anything.
                OrderBookStatus.Paused,
                new TradingPhase(new AuctionMatchingAlgorithm(), AcceptsOrderActions: true, AcceptsMarketOrders: false,
                    MatchesContinuously: false, StartsSession: false, ExpiresDayOrders: false)
            },
            {
                // No algorithm, so nothing matches and no indicative quote is published -
                // withholding price discovery is what makes this a halt rather than a pause.
                // Order actions stay open so resting positions can still be managed, and the
                // day's orders survive: a halt is not a close. Nothing prints on the way out
                // either, so a caller wanting a reopening auction goes through PreOpen or
                // Paused rather than straight back to Open.
                OrderBookStatus.Halted,
                new TradingPhase(null, AcceptsOrderActions: true, AcceptsMarketOrders: false,
                    MatchesContinuously: false, StartsSession: false, ExpiresDayOrders: false)
            }
        };

    private TradingPhase CurrentPhase => _phases[_status];

    // Keyed by InternalId, not ExchangeOrderId - the latter changes across an order's life.
    private readonly Dictionary<long, InternalOrder> _orders = new();
    private readonly Dictionary<long, InternalOrder> _completedOrders = new();

    // every (companyId, clientOrderId) pair ever assigned by a client, permanently reserved -
    // used for per-client uniqueness checks, ownership enforcement, and Update/Cancel lookups
    private readonly Dictionary<(string CompanyId, string ClientOrderId), InternalOrder> _clientOrderIndex = new();

    private const int MaxClientOrderIdLength = 20;

    public OrderBook(Instrument instrument)
        : this(instrument, Adapt(instrument.PriceRestrictions))
    {
    }

    // Config in, enforcement out. The instrument describes what it trades under; this is the only
    // place that knows which adapter each description means, so a new restriction is a new arm
    // rather than a change to how books are constructed.
    private static IReadOnlyList<IPriceRestriction> Adapt(IReadOnlyList<PriceRestriction>? configs) =>
        configs == null
            ? Array.Empty<IPriceRestriction>()
            : configs.Select<PriceRestriction, IPriceRestriction>(config => config switch
            {
                OrderPriceBand band => new OrderPriceBandRestriction(band.BandTicks),
                VolatilityBand band => new VolatilityBandRestriction(band.RangeTicks, band.PauseFor,
                    band.Window, band.ExtendedRangeTicks),
                StaticPriceRange range => new StaticPriceRangeRestriction(range.RangeTicks, range.PauseFor),

                // Same adapter as VolatilityBand: a velocity limit is that range at a short
                // window, and the two configs exist to say which is meant, not to behave apart.
                VelocityLimit limit => new VolatilityBandRestriction(limit.RangeTicks, limit.PauseFor,
                    limit.Window),
                DailyPriceLimit limit => new DailyPriceLimitRestriction(limit.Width),
                CircuitBreaker breaker => new CircuitBreakerRestriction(breaker.Width, breaker.HaltFor),
                _ => throw new ArgumentException($"Unknown price restriction {config.GetType().Name}")
            }).ToList();

    // Restrictions supplied outright rather than derived from the instrument. Internal because it
    // is a seam, not an API: it exists so combinations an Instrument cannot yet describe - two
    // trade-scoped restrictions disagreeing about severity, say - can still be exercised.
    internal OrderBook(Instrument instrument, IReadOnlyList<IPriceRestriction> priceRestrictions)
    {
        _instrument = instrument;
        _priceRestrictions = priceRestrictions;
    }

    public string Symbol => _instrument.Symbol;
    public OrderBookStatus Status => _status;

    public IReadOnlyList<OrderBookEvent> Process(OrderBookAction action)
    {
        // The action's own stamp is the only time this book has, and every event below carries
        // it. An unstamped action is a caller that has not decided when its action happened,
        // which would otherwise land silently at DateTime.MinValue and expire every GTD order
        // in the book.
        var time = action.Time;
        if (time == default)
            throw new ArgumentException(
                $"{action.GetType().Name} has no Time. Set it when constructing the action, or " +
                "drive the book through a TimestampingOrderBook to have a clock stamp it.",
                nameof(action));

        // Refused rather than clamped: time running backwards means the caller has misordered
        // its stream, and quietly carrying on would leave a book whose state no replay of those
        // same actions could reproduce. Equal stamps are fine - a burst can share an instant.
        if (time < _lastActionTime)
            throw new ArgumentException(
                $"{action.GetType().Name} is stamped {time:O}, behind the previous action's " +
                $"{_lastActionTime:O}. Actions must arrive in time order.",
                nameof(action));

        _lastActionTime = time;

        // Before the action rather than after: an order arriving once the interruption has
        // elapsed should meet a resumed book, not the paused one it would otherwise land in.
        // Doing it here rather than only on AdvanceTime means a book being fed order flow
        // resumes on its own, without needing anything to poke it.
        var events = ResumeIfDue(time);
        events.AddRange(Handle(action, time));

        // Last, so it reports where the action left the book rather than any state it passed
        // through - a pre-open cancel that uncrosses the book withdraws the quote, and the
        // opening print withdraws it after the trades it produced.
        var quoteChange = TakeIndicativeQuoteChange(time);
        if (quoteChange != null)
            events.Add(quoteChange);

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
            PreOpenTrading s => UpdateStatus(OrderBookStatus.PreOpen, s.ReferencePrice, true, OrderBookStatusChangeReason.Requested, time),
            OpenTrading s => UpdateStatus(OrderBookStatus.Open, s.ReferencePrice, true, OrderBookStatusChangeReason.Requested, time),
            CloseTrading c => UpdateStatus(OrderBookStatus.Closed, null, c.EndsTradingDay, OrderBookStatusChangeReason.Requested, time),
            PauseTrading => UpdateStatus(OrderBookStatus.Paused, null, true, OrderBookStatusChangeReason.Requested, time),
            HaltTrading => UpdateStatus(OrderBookStatus.Halted, null, true, OrderBookStatusChangeReason.Requested, time),

            // Carries nothing and does nothing: the work is the due-interruption check every
            // Process already runs, and this is how a caller with no order flow reaches it.
            AdvanceTime => new List<OrderBookEvent>(),
            _ => throw new ArgumentException("Unknown order book action")
        };
    }

    // A timed interruption returns the book to whatever it interrupted. Cleared by any explicit
    // status change, so a session closing over a pause ends it rather than being undone by it.
    private List<OrderBookEvent> ResumeIfDue(DateTime time)
    {
        if (_resumeAt == null || time < _resumeAt.Value)
            return new List<OrderBookEvent>();

        // Stamped at the deadline rather than at the action that noticed it: a book paused until
        // 10:05 that sees nothing until 10:47 resumed at 10:05, and the tape should say so. The
        // state is the same either way - the resume runs before the arriving action, so the
        // auction uncrosses against the book as of the deadline - and this only fixes what the
        // events say about when. Poked punctually the two instants coincide, so it is a book
        // driven directly, without anything ticking it, that this is for.
        //
        // An event stamped behind the action carrying it cannot trip the monotonicity guard,
        // which checks inbound actions rather than emitted events.
        var due = _resumeAt.Value;
        _resumeAt = null;
        return UpdateStatus(_resumeTo, null, true, OrderBookStatusChangeReason.InterruptionElapsed, due);
    }

    // Asked of the phase's own algorithm, so a quote exists exactly when there is an auction
    // to report one for - the start-of-day session or a volatility pause. Continuous trading
    // declines (price-time prints at as many prices as a sweep touches, not one), as does an
    // uncrossed book and a phase with no algorithm at all.
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

        // An anchor for anything banding against the auction price, withdrawn along with the
        // quote. Reaching restrictions here rather than at order entry means an order is judged
        // against the quote as it stood before that order - which is the only thing it could be
        // judged against, since the quote cannot account for an order that has not arrived.
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
        if (validity is OrderValidity.GoodTilDate { Date: var goodTilDate } && goodTilDate < DateOnly.FromDateTime(time))
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

    // Client-supplied resting limit prices only. Trigger prices are governed by the
    // TriggerPriceMustBe... checks above, and Market/MarketLimit prices by
    // MarketOrderProtectionTicks.
    // Null when every entry-scoped restriction allows the price, otherwise the rejection the
    // first refusing one asks for - a band and a daily limit turn an order away for reasons
    // that read differently to whoever sent it.
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

    // A stop elected far from its trigger would rest at a price the band would never have
    // accepted directly, so CME bounds the gap by the same band. Checked on the pair rather
    // than on either price, and only where a band exists to bound it.
    private bool AllowsStopSpread(long triggerTicks, long priceTicks) =>
        _priceRestrictions.Where(r => r.Scope.HasFlag(RestrictionScope.OrderEntry))
            .All(r => r.AllowsStopSpread(Math.Abs(priceTicks - triggerTicks)));

    // Only ever called on an order currently resting in the working book (a FAK remainder or
    // a self-match-prevention cancel during Match()) - never a still-Hidden stop order.
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
            // ignore trigger price if already triggered
            triggerTicks = null;
        }

        // TODO: can't update price on stop market order?

        // Captured before any mutation below. previousPrice is null when the order isn't
        // currently resting in the working book (still Hidden) - the working-book level
        // aggregate treats that case as an arrival, not a move.
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
        // For an iceberg order, MaxVisibleQuantity (the peak) is immutable, so any quantity
        // increase here can only be growing the hidden reserve - CME/Eurex don't lose
        // priority for that, only for a peak increase, which isn't possible in this scope.
        var isQuantityIncrease = order.MaxVisibleQuantity == null &&
            (newTotalQuantity != null && newTotalQuantity > order.Quantity);

        if (isPriceChange || isQuantityIncrease)
        {
            _nextSequenceNumber++;
            sequenceNumber = _nextSequenceNumber;
            var updatedPriceTicks =
                (order.Status == OrderStatus.Hidden ? triggerTicks ?? order.TriggerPrice : priceTicks ?? order.Price) ??
                throw new InvalidOperationException("missing price");

            // Called before order.Update() below, so the order still carries the price it
            // currently rests at - which is what Reprice moves it off.
            _matcher.Reprice(order, updatedPriceTicks);
        }

        // captured before Update() below, which - since sequenceNumber may have just been
        // bumped above - is where ExchangeOrderId (derived from SequenceNumber) actually changes.
        var previousExchangeOrderId = order.ExchangeOrderId;
        order.Update(sequenceNumber, time, newTotalQuantity, triggerTicks, priceTicks, clientOrderId);
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
        _completedOrders.Add(order.InternalId, order);
    }

    // Called immediately after order.Fill(...). Snapshot the order for FillOrderConfirmed
    // before calling: a replenish changes ExchangeOrderId, and the fill was against the old one.
    private OrderBookEvent? FinishFill(InternalOrder order, DateTime time)
    {
        if (order.Status == OrderStatus.Filled)
        {
            CompleteOrder(order);
            return null;
        }

        if (order.DisplayedQuantity == 0 && order.MaxVisibleQuantity.HasValue)
        {
            // Peak exhausted with reserve remaining. Requeues to the back of the level with a
            // fresh ExchangeOrderId, as CME and Eurex both do - so a full-book feed sees the
            // old id leave and a new one arrive rather than an in-place modify.
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

    // The single gate on whether trading may happen right now, which is why an exiting
    // auction's print goes through it too: a phase left for one that does not trade abandons
    // the orders it accumulated rather than crossing them.
    private void Match(List<OrderBookEvent> events, IMatchingAlgorithm? algorithm = null, DateTime time = default)
    {
        var phase = CurrentPhase;
        if (!phase.MatchesContinuously)
        {
            return;
        }

        var continuous = phase.Algorithm ??
            throw new InvalidOperationException("a phase that matches continuously needs an algorithm");
        var pendingImmediateOrCancelStops = new List<InternalOrder>();

        // Closed over the action's instant rather than passed as a method group, so a sweep
        // judges every price it touches against the same moment the events it emits are stamped
        // with. A restriction reading a clock here instead would drift within a single action.
        foreach (var outcome in _matcher.Run(algorithm ?? continuous, continuous,
                     priceTicks => CheckTradeRestrictionBreach(priceTicks, time)))
            Apply(outcome, events, time, pendingImmediateOrCancelStops);

        // Deferred until the sweep is done: the loop only exits once nothing crosses anywhere,
        // so "did it fill" cannot be answered any earlier.
        foreach (var order in pendingImmediateOrCancelStops)
        {
            if (order.RemainingQuantity > 0)
                events.Add(CancelRemainder(order, OrderCancelledReason.ImmediateOrCancelNotFilled, time));
        }
    }

    // The severest consequence among the Trade-scoped restrictions that disallow priceTicks; a
    // pure query, consulted by Matcher.Run only outside an auction uncrossing pass. Severest
    // rather than first, so the order these are declared in cannot decide whether a breach that
    // halts is served or shadowed by one that merely pauses.
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

    // Consequence first, then how long it lasts - a price through a circuit breaker's widest
    // level is through its narrower ones too, and the market should be halted for as long as
    // the level it actually reached says rather than the one it passed on the way. Never
    // resuming outranks any duration, which is what the level that ends a trading day is.
    private static bool IsMoreSevere(RestrictionBreach candidate, RestrictionBreach current)
    {
        if (Severity(candidate.Action) != Severity(current.Action))
            return Severity(candidate.Action) > Severity(current.Action);

        if (!candidate.ResumeAfter.HasValue || !current.ResumeAfter.HasValue)
            return !candidate.ResumeAfter.HasValue && current.ResumeAfter.HasValue;

        return candidate.ResumeAfter.Value > current.ResumeAfter.Value;
    }

    // Whether a restriction refuses to let the interruption the book is in end at the price it
    // would end at. Eurex extends a volatility interruption rather than resolving it at a price
    // still too far out; without a restriction configured for that, this always declines to
    // interfere and every transition goes ahead.
    //
    // Only where a print is what would end it: a phase leaving for one that does not trade
    // abandons its orders rather than crossing them, so there is no price to hold to anything -
    // and a close must never be blocked by a price range.
    private RestrictionBreach? CheckResumptionRefusal(OrderBookStatus arrivingStatus, DateTime time)
    {
        var departing = CurrentPhase;
        if (!departing.PrintsOnExit || !_phases[arrivingStatus].MatchesContinuously)
            return null;

        if (!departing.Algorithm!.TryQuoteIndicative(_matcher.Working, out var priceTicks, out _))
            return null;

        foreach (var restriction in _priceRestrictions)
        {
            // First refusal rather than the severest: every restriction refusing here is asking
            // for the same thing, so there is nothing to rank.
            if (restriction.Scope.HasFlag(RestrictionScope.Trade) &&
                !restriction.AllowsResumption(priceTicks, time))
                return new RestrictionBreach(restriction.OnBreach, restriction.ResumeAfter);
        }

        return null;
    }

    // Ranked explicitly rather than leaning on the enum's declaration order, which is free to
    // change. Reject never reaches here - it is an order-entry consequence.
    private static int Severity(RestrictionBreachAction action) => action switch
    {
        RestrictionBreachAction.Halt => 3,
        RestrictionBreachAction.Pause => 2,

        // Below both: a limit-locked market is still open and still trading, at the limit. It
        // is the mildest thing that can stop a sweep, not a form of interruption.
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

            // A limit stops the sweep and nothing else: the market is open, quoting, and can
            // trade at the limit and back inside it. Only the status is spared - the run has
            // already ended, since Matcher.Run stops on any breach.
            case TradeRestrictionBreached(var blockedTicks, {Action: RestrictionBreachAction.Block}):
                TakeLimitStateChange(blockedTicks, events, time);
                break;

            // Assigned directly rather than routed through UpdateStatus: this runs inside the
            // Run/Apply loop, and UpdateStatus matches, which would re-enter the matcher
            // mid-enumeration. The phases these land on are deliberately ones with nothing to
            // do on arrival anyway - neither starts a session nor expires orders.
            case TradeRestrictionBreached(_, var breach):
                // Captured before the overwrite: an interruption returns to whatever it
                // interrupted, which is the only phase that could have been matching.
                _resumeTo = _status;
                _status = breach.Action == RestrictionBreachAction.Halt
                    ? OrderBookStatus.Halted
                    : OrderBookStatus.Paused;
                _resumeAt = breach.ResumeAfter.HasValue ? time + breach.ResumeAfter.Value : null;
                events.Add(new StatusChanged(_instrument.Symbol, time, _status, OrderBookStatusChangeReason.PriceRestriction,
                    _resumeAt));
                break;

            case StopsTriggered(var orders):
                TriggerStops(orders, time, events, pendingImmediateOrCancelStops);
                break;
        }
    }

    // Which way the market is stuck, and only when that changes - a limit-locked book refuses
    // every sweep that follows, and saying so once is enough. Direction comes from where the
    // blocked price sits against the last one that traded; with nothing traded yet there is
    // nothing to compare it to, and the price alone says where the limit is.
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
        events.Add(new LimitStateChanged(_instrument.Symbol, time, side, ToDecimal(blockedTicks)));
    }

    // A print means the market is trading again, wherever it is trading. Releasing on any trade
    // rather than on one strictly inside the limits is deliberate: trading at the limit is the
    // market working, not the market stuck.
    private void ReleaseLimitState(List<OrderBookEvent> events, DateTime time)
    {
        if (_limitState == null)
            return;

        _limitState = null;
        events.Add(new LimitStateChanged(_instrument.Symbol, time, null, null));
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
        var restingSnapshot = resting.ToOrder();
        var restingReplenish = FinishFill(resting, time);

        var aggressorDisplayed = aggressor.DisplayedQuantity;
        FillOrder(aggressor);
        var aggressorSnapshot = aggressor.ToOrder();
        var aggressorReplenish = FinishFill(aggressor, time);

        events.Add(new OrdersMatched(_instrument.Symbol, time, price, quantity,
            new[]
            {
                new FillOrderConfirmed(_instrument.Symbol, time, resting.CompanyId, restingSnapshot, price, quantity,
                    restingDisplayed, true),
                new FillOrderConfirmed(_instrument.Symbol, time, aggressor.CompanyId, aggressorSnapshot, price,
                    quantity, aggressorDisplayed, false)
            }
        ));

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
            // Lifted out while still typed as a stop; converted or cancelled below.
            _matcher.Unrest(order);

            if (order.Validity is OrderValidity.ImmediateOrCancel)
                pendingImmediateOrCancelStops.Add(order);

            // calculate price for stop market orders
            long? newPriceTicks = order.Price;
            if (order.Type == OrderType.StopMarket &&
                !TryGetLimitPrice(order.Side, _instrument.MarketOrderProtectionTicks, out newPriceTicks))
            {
                var previousClientOrderId = order.ClientOrderId;
                var previousQuantity = order.DisplayedQuantity;
                order.Cancel(time);
                FinishOrder(order);

                // FinishOrder, not CompleteOrder: already unrested above and never reached the
                // working book, which is also why previousPrice is null.
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

            // Retyped as a limit order, so this rests it in the working book.
            _matcher.Rest(order);

            // previousPrice null - an arrival, not a move between working-book levels.
            events.Add(new UpdateOrderConfirmed(_instrument.Symbol, time, order.CompanyId, order.ToOrder(),
                order.ClientOrderId, previousExchangeOrderId, null, order.DisplayedQuantity));
        }
    }

    // endsTradingDay qualifies a close: several sessions can share one trading day, and only the
    // last of them ends it. The phase table stays the authority on whether a phase expires day
    // orders at all - this only says whether this particular close is that day's last.
    private List<OrderBookEvent> UpdateStatus(OrderBookStatus status, decimal? referencePrice = null,
        bool endsTradingDay = true, OrderBookStatusChangeReason reason = OrderBookStatusChangeReason.Requested, DateTime time = default)
    {
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
            // Nothing has moved yet, so refusing simply leaves the book where it was. The status
            // is unchanged and re-reported: what a subscriber needs to know is that the
            // interruption is still running and why, which is the same thing a fresh one says.
            _resumeAt = extension.Value.ResumeAfter.HasValue ? time + extension.Value.ResumeAfter.Value : null;
            return new List<OrderBookEvent>
                {new StatusChanged(_instrument.Symbol, time, _status, OrderBookStatusChangeReason.PriceRestriction, _resumeAt)};
        }

        // Any transition supersedes a pending one, so a session closing over a running pause
        // ends it rather than being undone when that pause's deadline arrives.
        _resumeAt = null;

        var departing = CurrentPhase;
        _status = status;
        var arriving = CurrentPhase;

        if (arriving.StartsSession)
        {
            // Seeded from the date so an id carries the day it was issued, but only ever
            // forwards: a second session on the same date computes a seed the counter has
            // already passed and so continues from where it was. Restarting it would re-issue
            // ids that orders surviving the previous session (GTC, or GTD not yet due) still
            // hold, and _orders/_completedOrders are keyed on exactly that. Math.Max is also
            // what keeps a replay whose clock moves backwards from colliding - a run of ids
            // that no longer encodes its date beats one that repeats itself.
            var seed = ((time.Year * 10000) + (time.Month * 100) + time.Day) * 10000000000L;
            _nextSequenceNumber = Math.Max(_nextSequenceNumber, seed);
        }

        // _resumeAt was cleared above, so this reports nothing pending - which is what an
        // explicit transition means, having just superseded whatever was.
        var events = new List<OrderBookEvent> {new StatusChanged(_instrument.Symbol, time, _status, reason, _resumeAt)};

        // A quoting phase has been accumulating orders for a print, and leaving it is where
        // that print happens - so a second auction phase would need nothing changed here.
        // Match declines it if the phase just entered does not trade.
        if (departing.PrintsOnExit)
            Match(events, departing.Algorithm, time);

        // Then trading continues under whatever governs the phase just entered.
        Match(events, time: time);

        if (arriving.ExpiresDayOrders && endsTradingDay)
            events.AddRange(ExpireOrders(time));

        return events;
    }

    private IEnumerable<OrderBookEvent> ExpireOrders(DateTime time)
    {
        var today = DateOnly.FromDateTime(time);
        var orders = _orders.Values.Where(o =>
            o.Validity is OrderValidity.Day ||
            (o.Validity is OrderValidity.GoodTilDate { Date: var date } && date <= today))
            .OrderBy(o => o.InternalId)
            .ToList();

        return orders.Select(o => ExpireOrder(o, time)).ToList();
    }
}
