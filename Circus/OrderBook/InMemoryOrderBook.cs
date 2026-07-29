using System;
using System.Collections.Generic;
using System.Linq;
using Circus.TimeProviders;

namespace Circus.OrderBook
{
    public class InMemoryOrderBook : IOrderBook
    {
        private readonly Security _security;
        private readonly ITimeProvider _timeProvider;

        private OrderBookStatus _status = OrderBookStatus.Closed;
        private long _nextSequenceNumber;
        private long? _lastTradedPrice;

        // A timed interruption's deadline and where it returns to. Null whenever the book is not
        // serving one, which includes an interruption configured to last until told otherwise.
        private DateTime? _resumeAt;
        private OrderBookStatus _resumeTo;

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
                    new TradingPhase(new Auction(), AcceptsOrderActions: true, AcceptsMarketOrders: false,
                        MatchesContinuously: false, StartsSession: true, ExpiresDayOrders: false)
                },
                {
                    OrderBookStatus.Open,
                    new TradingPhase(new PriceTime(), AcceptsOrderActions: true, AcceptsMarketOrders: true,
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
                    new TradingPhase(new Auction(), AcceptsOrderActions: true, AcceptsMarketOrders: false,
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

        public InMemoryOrderBook(Security security, ITimeProvider timeProvider)
            : this(security, timeProvider, Adapt(security.PriceRestrictions))
        {
        }

        // Config in, enforcement out. The security describes what it trades under; this is the only
        // place that knows which adapter each description means, so a new restriction is a new arm
        // rather than a change to how books are constructed.
        private static IReadOnlyList<IPriceRestriction> Adapt(IReadOnlyList<PriceRestrictionConfig>? configs) =>
            configs == null
                ? Array.Empty<IPriceRestriction>()
                : configs.Select<PriceRestrictionConfig, IPriceRestriction>(config => config switch
                {
                    OrderPriceBand band => new OrderPriceRestriction(band.BandTicks),
                    VolatilityBand band => new VolatilityBandRestriction(band.BandTicks, band.PauseFor),
                    _ => throw new ArgumentException($"Unknown price restriction {config.GetType().Name}")
                }).ToList();

        // Restrictions supplied outright rather than derived from the security. Internal because it
        // is a seam, not an API: it exists so combinations a Security cannot yet describe - two
        // trade-scoped restrictions disagreeing about severity, say - can still be exercised.
        internal InMemoryOrderBook(Security security, ITimeProvider timeProvider,
            IReadOnlyList<IPriceRestriction> priceRestrictions)
        {
            _security = security;
            _timeProvider = timeProvider;
            _priceRestrictions = priceRestrictions;
        }

        private DateTime Now() => _timeProvider.GetCurrentTime();

        public Security Security => _security;
        public OrderBookStatus Status => _status;

        public IReadOnlyList<OrderBookEvent> Process(OrderBookAction action)
        {
            // Before the action rather than after: an order arriving once the interruption has
            // elapsed should meet a resumed book, not the paused one it would otherwise land in.
            // Doing it here rather than only on AdvanceTime means a book being fed order flow
            // resumes on its own, without needing anything to poke it.
            var events = ResumeIfDue();
            events.AddRange(Handle(action));

            // Last, so it reports where the action left the book rather than any state it passed
            // through - a pre-open cancel that uncrosses the book withdraws the quote, and the
            // opening print withdraws it after the trades it produced.
            var quoteChange = TakeIndicativeQuoteChange();
            if (quoteChange != null)
                events.Add(quoteChange);

            return events;
        }

        private List<OrderBookEvent> Handle(OrderBookAction action)
        {
            return action switch
            {
                CreateLimitOrder o => CreateOrder(o.CompanyId, o.ClientOrderId, o.OrderValidity, o.Side, o.Quantity,
                    OrderType.Limit, o.Price, null, o.SelfMatchPrevention, o.MaxVisibleQuantity),
                CreateMarketOrder o => CreateOrder(o.CompanyId, o.ClientOrderId, o.OrderValidity, o.Side, o.Quantity,
                    OrderType.Market, null, null, o.SelfMatchPrevention, o.MaxVisibleQuantity),
                CreateMarketLimitOrder o => CreateOrder(o.CompanyId, o.ClientOrderId, o.OrderValidity, o.Side,
                    o.Quantity, OrderType.MarketLimit, null, null, o.SelfMatchPrevention, o.MaxVisibleQuantity),
                CreateStopLimitOrder o => CreateOrder(o.CompanyId, o.ClientOrderId, o.OrderValidity, o.Side,
                    o.Quantity, OrderType.StopLimit, o.Price, o.TriggerPrice, o.SelfMatchPrevention, o.MaxVisibleQuantity),
                CreateStopMarketOrder o => CreateOrder(o.CompanyId, o.ClientOrderId, o.OrderValidity, o.Side,
                    o.Quantity, OrderType.StopMarket, null, o.TriggerPrice, o.SelfMatchPrevention, o.MaxVisibleQuantity),
                UpdateOrder update => UpdateOrder(update.CompanyId, update.ClientOrderId,
                    update.PreviousClientOrderId, update.NewTotalQuantity, update.Price, update.TriggerPrice),
                CancelOrder cancel => CancelOrder(cancel.CompanyId, cancel.ClientOrderId, cancel.PreviousClientOrderId),
                PreOpenTrading s => UpdateStatus(OrderBookStatus.PreOpen, s.ReferencePrice),
                OpenTrading s => UpdateStatus(OrderBookStatus.Open, s.ReferencePrice),
                CloseTrading c => UpdateStatus(OrderBookStatus.Closed, null, c.EndsTradingDay),
                PauseTrading => UpdateStatus(OrderBookStatus.Paused),
                HaltTrading => UpdateStatus(OrderBookStatus.Halted),

                // Carries nothing and does nothing: the work is the due-interruption check every
                // Process already runs, and this is how a caller with no order flow reaches it.
                AdvanceTime => new List<OrderBookEvent>(),
                _ => throw new ArgumentException("Unknown order book action")
            };
        }

        // A timed interruption returns the book to whatever it interrupted. Cleared by any explicit
        // status change, so a session closing over a pause ends it rather than being undone by it.
        private List<OrderBookEvent> ResumeIfDue()
        {
            if (_resumeAt == null || Now() < _resumeAt.Value)
                return new List<OrderBookEvent>();

            _resumeAt = null;
            return UpdateStatus(_resumeTo, reason: StatusChangeReason.InterruptionElapsed);
        }

        // Asked of the phase's own algorithm, so a quote exists exactly when there is an auction
        // to report one for - the start-of-day session or a volatility pause. Continuous trading
        // declines (price-time prints at as many prices as a sweep touches, not one), as does an
        // uncrossed book and a phase with no algorithm at all.
        private OrderBookEvent? TakeIndicativeQuoteChange()
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

            return new IndicativePriceChanged(_security, Now(),
                quote.HasValue ? (decimal?) ToDecimal(quote.Value.PriceTicks) : null, quote?.Quantity ?? 0);
        }

        private List<OrderBookEvent> CreateOrder(string companyId, string clientOrderId, OrderValidity validity,
            Side side, int quantity, OrderType type, decimal? price = null, decimal? triggerPrice = null,
            SelfMatchPrevention? selfMatchPrevention = null, int? maxVisibleQuantity = null)
        {
            var selfMatchPreventionId = selfMatchPrevention?.Id;
            var selfMatchPreventionInstruction = selfMatchPrevention?.Instruction;
            var status = triggerPrice.HasValue ? OrderStatus.Hidden : OrderStatus.Working;

            if (!CurrentPhase.AcceptsOrderActions)
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.MarketClosed);
            if (type == OrderType.Market && !CurrentPhase.AcceptsMarketOrders)
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.MarketOrdersNotAccepted);
            if (string.IsNullOrEmpty(clientOrderId))
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.ClientOrderIdRequired);
            if (clientOrderId.Length > MaxClientOrderIdLength)
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.ClientOrderIdTooLong);
            if (string.IsNullOrEmpty(companyId))
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.CompanyIdRequired);
            if (companyId.Length > MaxClientOrderIdLength)
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.CompanyIdTooLong);
            if (selfMatchPreventionId != null && selfMatchPreventionId.Length > MaxClientOrderIdLength)
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.SelfMatchPreventionIdTooLong);
            if (quantity < 1)
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.InvalidQuantity);
            if (!TryConvertToTicks(price, out var priceTicks))
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.InvalidPriceIncrement);
            if (!TryConvertToTicks(triggerPrice, out var triggerTicks))
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.InvalidPriceIncrement);
            if (triggerTicks != null && priceTicks != null && side == Side.Buy && priceTicks < triggerTicks)
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.TriggerPriceMustBeLessThanPrice);
            if (triggerTicks != null && priceTicks != null && side == Side.Sell && priceTicks > triggerTicks)
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.TriggerPriceMustBeGreaterThanPrice);
            if (triggerTicks != null && priceTicks != null &&
                !AllowsStopSpread(triggerTicks.Value, priceTicks.Value))
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.TriggerPriceTooFarFromPrice);
            if (triggerTicks != null && !_lastTradedPrice.HasValue)
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.NoLastTradedPrice);
            if (triggerTicks != null && side == Side.Buy && triggerTicks <= _lastTradedPrice)
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.TriggerPriceMustBeGreaterThanLastTradedPrice);
            if (triggerTicks != null && side == Side.Sell && triggerTicks >= _lastTradedPrice)
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.TriggerPriceMustBeLessThanLastTradedPrice);
            if (priceTicks.HasValue && !AllowsOrderEntry(priceTicks.Value))
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.PriceOutsideBands);
            if (validity is OrderValidity.ImmediateOrCancel { MinQuantity: int minQty } && (minQty < 1 || minQty > quantity))
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.MinQuantityOutOfRange);
            if (maxVisibleQuantity.HasValue && (maxVisibleQuantity < 1 || maxVisibleQuantity > quantity))
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.MaxVisibleQuantityOutOfRange);
            if (_clientOrderIndex.TryGetValue((companyId, clientOrderId), out var existingOrder))
            {
                return existingOrder.Status is OrderStatus.Working or OrderStatus.Hidden
                    ? RejectCreate(companyId, clientOrderId, OrderRejectedReason.OrderInBook)
                    : RejectCreate(companyId, clientOrderId, OrderRejectedReason.OrderIdAlreadyUsed);
            }

            if (type == OrderType.Market || type == OrderType.MarketLimit)
            {
                var protectionTicks = type == OrderType.MarketLimit ? 0 : _security.MarketOrderProtectionTicks;
                if(!TryGetLimitPrice(side, protectionTicks, out priceTicks))
                    return RejectCreate(companyId, clientOrderId, OrderRejectedReason.NoOrdersToMatchMarketOrder);
            }

            if (validity is OrderValidity.ImmediateOrCancel { MinQuantity: int gateMinQty } && !triggerTicks.HasValue &&
                !_matcher.HasSufficientLiquidity(side, priceTicks!.Value, gateMinQty, selfMatchPreventionId,
                    selfMatchPreventionInstruction))
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.InsufficientLiquidityForMinQuantity);
            if (validity is OrderValidity.GoodTilDate { Date: var goodTilDate } && goodTilDate < DateOnly.FromDateTime(Now()))
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.InvalidExpireDate);

            _nextSequenceNumber++;
            var order = new InternalOrder(_nextSequenceNumber, companyId, clientOrderId, _security, Now(), status,
                type, validity, side, quantity, priceTicks, triggerTicks, selfMatchPreventionId,
                selfMatchPreventionInstruction, maxVisibleQuantity);

            _orders.Add(order.InternalId, order);
            _clientOrderIndex.Add((companyId, clientOrderId), order);
            _matcher.Rest(order);

            List<OrderBookEvent> events = new();
            events.Add(new CreateOrderConfirmed(_security, Now(), companyId, order.ToOrder()));
            Match(events);

            if (order.Validity is OrderValidity.ImmediateOrCancel && order.Status == OrderStatus.Working)
                events.Add(CancelRemainder(order, OrderCancelledReason.ImmediateOrCancelNotFilled));

            return events;
        }

        private bool TryConvertToTicks(decimal? price, out long? ticks)
        {
            if (!price.HasValue)
            {
                ticks = null;
                return true;
            }

            var rawTicks = price.Value / _security.TickSize;
            var truncatedTicks = Math.Truncate(rawTicks);
            if (rawTicks != truncatedTicks)
            {
                ticks = null;
                return false;
            }

            ticks = (long) truncatedTicks;
            return true;
        }

        private decimal ToDecimal(long ticks) => ticks * _security.TickSize;

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
        private bool AllowsOrderEntry(long priceTicks) =>
            _priceRestrictions.Where(r => r.Scope == RestrictionScope.OrderEntry)
                .All(r => r.Allows(priceTicks, Now()));

        // A stop elected far from its trigger would rest at a price the band would never have
        // accepted directly, so CME bounds the gap by the same band. Checked on the pair rather
        // than on either price, and only where a band exists to bound it.
        private bool AllowsStopSpread(long triggerTicks, long priceTicks) =>
            _priceRestrictions.Where(r => r.Scope == RestrictionScope.OrderEntry)
                .All(r => r.AllowsStopSpread(Math.Abs(priceTicks - triggerTicks)));

        // Only ever called on an order currently resting in the working book (a FAK remainder or
        // a self-match-prevention cancel during Match()) - never a still-Hidden stop order.
        private OrderBookEvent CancelRemainder(InternalOrder order, OrderCancelledReason reason)
        {
            var previousClientOrderId = order.ClientOrderId;
            var previousPrice = ToDecimal(order.Price!.Value);
            var previousQuantity = order.DisplayedQuantity;
            order.Cancel(Now());
            CompleteOrder(order);
            return new CancelOrderConfirmed(_security, Now(), order.CompanyId, order.ToOrder(), previousClientOrderId,
                reason, previousPrice, previousQuantity);
        }

        private List<OrderBookEvent> UpdateOrder(string companyId, string clientOrderId, string previousClientOrderId,
            int? newTotalQuantity = null, decimal? price = null, decimal? triggerPrice = null)
        {
            if (!CurrentPhase.AcceptsOrderActions)
                return RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.MarketClosed);
            if (string.IsNullOrEmpty(clientOrderId))
                return RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.ClientOrderIdRequired);
            if (clientOrderId.Length > MaxClientOrderIdLength)
                return RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.ClientOrderIdTooLong);
            if (string.IsNullOrEmpty(companyId))
                return RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.CompanyIdRequired);
            if (companyId.Length > MaxClientOrderIdLength)
                return RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.CompanyIdTooLong);
            if (newTotalQuantity == null && price == null && triggerPrice == null)
                return RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.NoChange);
            if (newTotalQuantity != null && newTotalQuantity < 1)
                return RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.InvalidQuantity);
            if (!TryConvertToTicks(price, out var priceTicks))
                return RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.InvalidPriceIncrement);
            if (!TryConvertToTicks(triggerPrice, out var triggerTicks))
                return RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.InvalidPriceIncrement);
            if (priceTicks.HasValue && !AllowsOrderEntry(priceTicks.Value))
                return RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.PriceOutsideBands);
            if (!_clientOrderIndex.TryGetValue((companyId, previousClientOrderId), out var order) ||
                order.ClientOrderId != previousClientOrderId)
                return RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.OrderNotInBook);
            if (order.Status is not (OrderStatus.Working or OrderStatus.Hidden))
                return RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.TooLateToCancel,
                    order.ExchangeOrderId);
            if (_clientOrderIndex.TryGetValue((companyId, clientOrderId), out var conflictingOrder))
            {
                return conflictingOrder.Status is OrderStatus.Working or OrderStatus.Hidden
                    ? RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.OrderInBook,
                        order.ExchangeOrderId)
                    : RejectUpdate(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.OrderIdAlreadyUsed,
                        order.ExchangeOrderId);
            }

            if (order.Status == OrderStatus.Hidden)
            {
                var newTriggerTicks = triggerTicks ?? order.TriggerPrice;
                var newPriceTicks = priceTicks ?? order.Price;

                if (newTriggerTicks != null && newPriceTicks != null && order.Side == Side.Buy && newPriceTicks < newTriggerTicks)
                    return RejectUpdate(companyId, clientOrderId, previousClientOrderId,
                        OrderRejectedReason.TriggerPriceMustBeLessThanPrice, order.ExchangeOrderId);
                if (newTriggerTicks != null && newPriceTicks != null && order.Side == Side.Sell && newPriceTicks > newTriggerTicks)
                    return RejectUpdate(companyId, clientOrderId, previousClientOrderId,
                        OrderRejectedReason.TriggerPriceMustBeGreaterThanPrice, order.ExchangeOrderId);
                if (newTriggerTicks != null && newPriceTicks != null &&
                    !AllowsStopSpread(newTriggerTicks.Value, newPriceTicks.Value))
                    return RejectUpdate(companyId, clientOrderId, previousClientOrderId,
                        OrderRejectedReason.TriggerPriceTooFarFromPrice, order.ExchangeOrderId);

                if (triggerTicks != null && order.Side == Side.Buy && triggerTicks <= _lastTradedPrice)
                    return RejectUpdate(companyId, clientOrderId, previousClientOrderId,
                        OrderRejectedReason.TriggerPriceMustBeGreaterThanLastTradedPrice, order.ExchangeOrderId);
                if (triggerTicks != null && order.Side == Side.Sell && triggerTicks >= _lastTradedPrice)
                    return RejectUpdate(companyId, clientOrderId, previousClientOrderId,
                        OrderRejectedReason.TriggerPriceMustBeLessThanLastTradedPrice, order.ExchangeOrderId);
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
                order.Cancel(Now(), clientOrderId);
                _clientOrderIndex[(companyId, clientOrderId)] = order;
                CompleteOrder(order);

                return new List<OrderBookEvent>
                {
                    new CancelOrderConfirmed(_security, Now(), order.CompanyId, order.ToOrder(), previousClientOrderId,
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
            order.Update(sequenceNumber, Now(), newTotalQuantity, triggerTicks, priceTicks, clientOrderId);
            _clientOrderIndex[(companyId, clientOrderId)] = order;

            List<OrderBookEvent> events = new();
            events.Add(new UpdateOrderConfirmed(_security, Now(), order.CompanyId, order.ToOrder(), previousClientOrderId,
                previousExchangeOrderId, previousPrice, previousQuantity));
            Match(events);
            return events;
        }

        private List<OrderBookEvent> CancelOrder(string companyId, string clientOrderId, string previousClientOrderId)
        {
            if (!CurrentPhase.AcceptsOrderActions)
                return RejectCancel(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.MarketClosed);
            if (string.IsNullOrEmpty(clientOrderId))
                return RejectCancel(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.ClientOrderIdRequired);
            if (clientOrderId.Length > MaxClientOrderIdLength)
                return RejectCancel(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.ClientOrderIdTooLong);
            if (string.IsNullOrEmpty(companyId))
                return RejectCancel(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.CompanyIdRequired);
            if (companyId.Length > MaxClientOrderIdLength)
                return RejectCancel(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.CompanyIdTooLong);
            if (!_clientOrderIndex.TryGetValue((companyId, previousClientOrderId), out var order) ||
                order.ClientOrderId != previousClientOrderId)
                return RejectCancel(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.OrderNotInBook);
            if (order.Status is not (OrderStatus.Working or OrderStatus.Hidden))
                return RejectCancel(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.TooLateToCancel,
                    order.ExchangeOrderId);
            if (_clientOrderIndex.TryGetValue((companyId, clientOrderId), out var conflictingOrder))
            {
                return conflictingOrder.Status is OrderStatus.Working or OrderStatus.Hidden
                    ? RejectCancel(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.OrderInBook,
                        order.ExchangeOrderId)
                    : RejectCancel(companyId, clientOrderId, previousClientOrderId, OrderRejectedReason.OrderIdAlreadyUsed,
                        order.ExchangeOrderId);
            }

            var previousPrice = order.Status == OrderStatus.Hidden ? (decimal?) null : ToDecimal(order.Price!.Value);
            var previousQuantity = order.DisplayedQuantity;
            order.Cancel(Now(), clientOrderId);
            _clientOrderIndex[(companyId, clientOrderId)] = order;
            CompleteOrder(order);

            return new List<OrderBookEvent>
            {
                new CancelOrderConfirmed(_security, Now(), order.CompanyId, order.ToOrder(), previousClientOrderId,
                    OrderCancelledReason.Cancelled, previousPrice, previousQuantity)
            };
        }

        private List<OrderBookEvent> RejectCreate(string companyId, string clientOrderId, OrderRejectedReason reason) =>
            new() {new CreateOrderRejected(_security, Now(), companyId, clientOrderId, reason)};

        private List<OrderBookEvent> RejectUpdate(string companyId, string clientOrderId, string previousClientOrderId,
                OrderRejectedReason reason, string? exchangeOrderId = null) =>
            new()
            {
                new UpdateOrderRejected(_security, Now(), companyId, clientOrderId, previousClientOrderId,
                    exchangeOrderId, reason)
            };

        private List<OrderBookEvent> RejectCancel(string companyId, string clientOrderId, string previousClientOrderId,
                OrderRejectedReason reason, string? exchangeOrderId = null) =>
            new()
            {
                new CancelOrderRejected(_security, Now(), companyId, clientOrderId, previousClientOrderId,
                    exchangeOrderId, reason)
            };

        private OrderBookEvent ExpireOrder(InternalOrder order)
        {
            var previousPrice = order.Status == OrderStatus.Hidden ? (decimal?) null : ToDecimal(order.Price!.Value);
            var previousQuantity = order.DisplayedQuantity;
            order.Expire(Now());
            CompleteOrder(order);

            return new ExpireOrderConfirmed(_security, Now(), order.CompanyId, order.ToOrder(), previousPrice,
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

                return new UpdateOrderConfirmed(_security, time, order.CompanyId, order.ToOrder(),
                    order.ClientOrderId, previousExchangeOrderId, ToDecimal(priceTicks), 0);
            }

            return null;
        }

        // The single gate on whether trading may happen right now, which is why an exiting
        // auction's print goes through it too: a phase left for one that does not trade abandons
        // the orders it accumulated rather than crossing them.
        private void Match(List<OrderBookEvent> events, IMatchingAlgorithm? algorithm = null)
        {
            var phase = CurrentPhase;
            if (!phase.MatchesContinuously)
            {
                return;
            }

            var continuous = phase.Algorithm ??
                throw new InvalidOperationException("a phase that matches continuously needs an algorithm");
            var time = Now();
            var pendingImmediateOrCancelStops = new List<InternalOrder>();

            foreach (var outcome in _matcher.Run(algorithm ?? continuous, continuous, CheckTradeRestrictionBreach))
                Apply(outcome, events, time, pendingImmediateOrCancelStops);

            // Deferred until the sweep is done: the loop only exits once nothing crosses anywhere,
            // so "did it fill" cannot be answered any earlier.
            foreach (var order in pendingImmediateOrCancelStops)
            {
                if (order.RemainingQuantity > 0)
                    events.Add(CancelRemainder(order, OrderCancelledReason.ImmediateOrCancelNotFilled));
            }
        }

        // The severest consequence among the Trade-scoped restrictions that disallow priceTicks; a
        // pure query, consulted by Matcher.Run only outside an auction uncrossing pass. Severest
        // rather than first, so the order these are declared in cannot decide whether a breach that
        // halts is served or shadowed by one that merely pauses.
        private RestrictionBreach? CheckTradeRestrictionBreach(long priceTicks)
        {
            var time = Now();
            RestrictionBreach? worst = null;

            foreach (var restriction in _priceRestrictions)
            {
                if (restriction.Scope != RestrictionScope.Trade || restriction.Allows(priceTicks, time))
                    continue;

                if (worst == null || Severity(restriction.OnBreach) > Severity(worst.Value.Action))
                    worst = new RestrictionBreach(restriction.OnBreach, restriction.ResumeAfter);
            }

            return worst;
        }

        // Ranked explicitly rather than leaning on the enum's declaration order, which is free to
        // change. Reject never reaches here - it is an order-entry consequence.
        private static int Severity(RestrictionBreachAction action) => action switch
        {
            RestrictionBreachAction.Halt => 2,
            RestrictionBreachAction.Pause => 1,
            _ => 0
        };

        private void Apply(MatchOutcome outcome, List<OrderBookEvent> events, DateTime time,
            List<InternalOrder> pendingImmediateOrCancelStops)
        {
            switch (outcome)
            {
                case SelfMatchDetected(var resting, var aggressor, var instruction):
                    if (instruction != SelfMatchPreventionInstruction.CancelAggressor)
                        events.Add(CancelRemainder(resting, OrderCancelledReason.SelfMatchPrevention));
                    if (instruction != SelfMatchPreventionInstruction.CancelResting)
                        events.Add(CancelRemainder(aggressor, OrderCancelledReason.SelfMatchPrevention));
                    break;

                case TradeExecuted(var resting, var aggressor, var priceTicks, var quantity, var usesFullRemainingQuantity):
                    ApplyTrade(resting, aggressor, priceTicks, quantity, usesFullRemainingQuantity, events, time);
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
                    _resumeAt = breach.ResumeAfter.HasValue ? Now() + breach.ResumeAfter.Value : null;
                    events.Add(new StatusChanged(_security, Now(), _status, StatusChangeReason.PriceRestriction));
                    break;

                case StopsTriggered(var orders):
                    TriggerStops(orders, time, events, pendingImmediateOrCancelStops);
                    break;
            }
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

            events.Add(new OrdersMatched(_security, time, price, quantity,
                new[]
                {
                    new FillOrderConfirmed(_security, time, resting.CompanyId, restingSnapshot, price, quantity,
                        restingDisplayed, true),
                    new FillOrderConfirmed(_security, time, aggressor.CompanyId, aggressorSnapshot, price,
                        quantity, aggressorDisplayed, false)
                }
            ));

            if (restingReplenish != null)
                events.Add(restingReplenish);
            if (aggressorReplenish != null)
                events.Add(aggressorReplenish);

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
                    !TryGetLimitPrice(order.Side, _security.MarketOrderProtectionTicks, out newPriceTicks))
                {
                    var previousClientOrderId = order.ClientOrderId;
                    var previousQuantity = order.DisplayedQuantity;
                    order.Cancel(Now());
                    FinishOrder(order);

                    // FinishOrder, not CompleteOrder: already unrested above and never reached the
                    // working book, which is also why previousPrice is null.
                    events.Add(new CancelOrderConfirmed(_security, Now(), order.CompanyId, order.ToOrder(),
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
                    order.Cancel(Now());
                    FinishOrder(order);

                    events.Add(new CancelOrderConfirmed(_security, Now(), order.CompanyId, order.ToOrder(),
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
                events.Add(new UpdateOrderConfirmed(_security, time, order.CompanyId, order.ToOrder(),
                    order.ClientOrderId, previousExchangeOrderId, null, order.DisplayedQuantity));
            }
        }

        // endsTradingDay qualifies a close: several sessions can share one trading day, and only the
        // last of them ends it. The phase table stays the authority on whether a phase expires day
        // orders at all - this only says whether this particular close is that day's last.
        private List<OrderBookEvent> UpdateStatus(OrderBookStatus status, decimal? referencePrice = null,
            bool endsTradingDay = true, StatusChangeReason reason = StatusChangeReason.Requested)
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
                var date = Now();
                var seed = ((date.Year * 10000) + (date.Month * 100) + date.Day) * 10000000000L;
                _nextSequenceNumber = Math.Max(_nextSequenceNumber, seed);
            }

            var events = new List<OrderBookEvent> {new StatusChanged(_security, Now(), _status, reason)};

            // A quoting phase has been accumulating orders for a print, and leaving it is where
            // that print happens - so a second auction phase would need nothing changed here.
            // Match declines it if the phase just entered does not trade.
            if (departing.PrintsOnExit)
                Match(events, departing.Algorithm);

            // Then trading continues under whatever governs the phase just entered.
            Match(events);

            if (arriving.ExpiresDayOrders && endsTradingDay)
                events.AddRange(ExpireOrders());

            return events;
        }

        private IEnumerable<OrderBookEvent> ExpireOrders()
        {
            var today = DateOnly.FromDateTime(Now());
            var orders = _orders.Values.Where(o =>
                o.Validity is OrderValidity.Day ||
                (o.Validity is OrderValidity.GoodTilDate { Date: var date } && date <= today)).ToList();

            return orders.Select(ExpireOrder).ToList();
        }
    }
}
