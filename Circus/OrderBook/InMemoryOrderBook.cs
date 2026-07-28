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

        // Reference anchor for the call-auction tie-break only (IsBetterAuctionPriceTieBreak):
        // seeded from an explicit reference price (mirroring CME's settlement price pre-open)
        // before any trade, then tracks the trade price. Kept separate from _lastTradedPrice,
        // which being null specifically means "no trade yet" for the stop-trigger checks. The
        // price restrictions no longer read this - each restriction owns its own anchor (see
        // IPriceRestriction.OnTrade / OnSessionChange), so nothing here is shared with them.
        private long? _auctionReferencePriceTicks;

        // Order-entry and trade-time price bands, each maintaining its own reference anchor.
        // A future velocity limit or circuit breaker is a new entry here, not a redesign.
        private readonly IReadOnlyList<IPriceRestriction> _priceRestrictions;

        // Owns the working/stop ladders and the pure decision helpers (auction pricing,
        // liquidity checks, self-match verdicts) that read them.
        private readonly Matcher _matcher = new();

        // Keyed by InternalId, not ExchangeOrderId - the latter changes across an order's life.
        private readonly Dictionary<long, InternalOrder> _orders = new();
        private readonly Dictionary<long, InternalOrder> _completedOrders = new();

        // every (companyId, clientOrderId) pair ever assigned by a client, permanently reserved -
        // used for per-client uniqueness checks, ownership enforcement, and Update/Cancel lookups
        private readonly Dictionary<(string CompanyId, string ClientOrderId), InternalOrder> _clientOrderIndex = new();

        private const int MaxClientOrderIdLength = 20;

        public InMemoryOrderBook(Security security, ITimeProvider timeProvider)
        {
            _security = security;
            _timeProvider = timeProvider;
            _priceRestrictions = new IPriceRestriction[]
            {
                new OrderPriceRestriction(security),
                new DailyPriceBandLimit(security)
            };
        }

        private DateTime Now() => _timeProvider.GetCurrentTime();

        public Security Security => _security;
        public OrderBookStatus Status => _status;

        public IReadOnlyList<Level> GetLevels(Side side, int maxPrices)
        {
            return _matcher.Working[side].EnumerateFromBest().Take(maxPrices)
                .Select(x => new Level(ToDecimal(x.Tick), SumDisplayed(x.First), x.Count))
                .ToList();
        }

        // Market data reports what's publicly visible - an iceberg's hidden reserve is
        // deliberately excluded here even though HasSufficientLiquidity/TryComputeAuctionPrice
        // (via SumRemaining) still count it in full for liquidity/price-discovery purposes.
        private static int SumDisplayed(InternalOrder? first)
        {
            var total = 0;
            for (var order = first; order != null; order = order.LevelNext)
                total += order.DisplayedQuantity;
            return total;
        }

        public bool TryGetIndicativeAuctionPrice(out decimal price, out int quantity)
        {
            if (!_matcher.TryComputeAuctionPrice(_auctionReferencePriceTicks, out var priceTicks, out quantity))
            {
                price = 0;
                return false;
            }

            price = ToDecimal(priceTicks);
            return true;
        }

        public IReadOnlyList<OrderBookEvent> Process(OrderBookAction action)
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
                CloseTrading => UpdateStatus(OrderBookStatus.Closed, null),
                _ => throw new ArgumentException("Unknown order book action")
            };
        }

        private List<OrderBookEvent> CreateOrder(string companyId, string clientOrderId, OrderValidity validity,
            Side side, int quantity, OrderType type, decimal? price = null, decimal? triggerPrice = null,
            SelfMatchPrevention? selfMatchPrevention = null, int? maxVisibleQuantity = null)
        {
            var selfMatchPreventionId = selfMatchPrevention?.Id;
            var selfMatchPreventionInstruction = selfMatchPrevention?.Instruction;
            var status = triggerPrice.HasValue ? OrderStatus.Hidden : OrderStatus.Working;

            if (_status == OrderBookStatus.Closed)
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.MarketClosed);
            if (type == OrderType.Market && _status == OrderBookStatus.PreOpen)
                return RejectCreate(companyId, clientOrderId, OrderRejectedReason.MarketPreOpen);
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
            var orders = (triggerTicks.HasValue ? _matcher.Stops : _matcher.Working);
            var newPriceTicks = (triggerTicks ?? priceTicks) ?? throw new Exception("error");
            orders[side].Add(newPriceTicks, order);

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

        // Only client-supplied resting limit prices go through this - not trigger prices (already
        // governed by the TriggerPriceMustBe.../LastTradedPrice checks above) and not the computed
        // effective price for Market/MarketLimit orders (already governed by the separate
        // MarketOrderProtectionTicks mechanism). Passes when every order-entry restriction allows
        // the price; each restriction is inactive (allows) until it has both a configured width
        // and an established reference.
        private bool AllowsOrderEntry(long priceTicks) =>
            _priceRestrictions.Where(r => r.Scope == RestrictionScope.OrderEntry).All(r => r.Allows(priceTicks));

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
            if (_status == OrderBookStatus.Closed)
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

            var orders = (order.Status == OrderStatus.Hidden ? _matcher.Stops : _matcher.Working);

            if (isPriceChange || isQuantityIncrease)
            {
                _nextSequenceNumber++;
                sequenceNumber = _nextSequenceNumber;
                var currentPriceTicks = (order.Status == OrderStatus.Hidden ? order.TriggerPrice : order.Price) ??
                                   throw new InvalidOperationException("missing price");
                var updatedPriceTicks =
                    (order.Status == OrderStatus.Hidden ? triggerTicks ?? order.TriggerPrice : priceTicks ?? order.Price) ??
                    throw new InvalidOperationException("missing price");
                orders[order.Side].Remove(currentPriceTicks, order);
                orders[order.Side].Add(updatedPriceTicks, order);
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
            if (_status == OrderBookStatus.Closed)
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
            if (order.Type == OrderType.StopLimit || order.Type == OrderType.StopMarket)
            {
                var price = order.TriggerPrice ?? throw new InvalidOperationException("stop order missing stop price");
                _matcher.Stops[order.Side].Remove(price, order);
            }
            else
            {
                var price = order.Price ?? throw new InvalidOperationException("limit order missing price");
                _matcher.Working[order.Side].Remove(price, order);
            }

            FinishOrder(order);
        }

        private void FinishOrder(InternalOrder order)
        {
            _orders.Remove(order.InternalId);
            _completedOrders.Add(order.InternalId, order);
        }

        // Called immediately after order.Fill(...) - completes the order if that fill finished it
        // off, or replenishes it if it's an iceberg whose displayed peak just hit zero with
        // hidden reserve still remaining. Returns the replenish event, if any; the caller is
        // responsible for snapshotting the order for FillOrderConfirmed before calling this,
        // since a replenish changes ExchangeOrderId and the fill happened against the old one.
        private OrderBookEvent? FinishFill(InternalOrder order, DateTime time)
        {
            if (order.Status == OrderStatus.Filled)
            {
                CompleteOrder(order);
                return null;
            }

            if (order.DisplayedQuantity == 0 && order.MaxVisibleQuantity.HasValue)
            {
                // iceberg peak exhausted with hidden reserve remaining - replenish and requeue to
                // the back of this price level (PriceLadder.Add always appends to the tail),
                // losing time priority and getting a fresh ExchangeOrderId - matches both CME and
                // Eurex, and lets a full-order-book feed show the old id leaving the book and a
                // new one arriving, rather than an in-place modify.
                var previousExchangeOrderId = order.ExchangeOrderId;
                var priceTicks = order.Price ?? throw new InvalidOperationException("limit order missing price");
                _matcher.Working[order.Side].Remove(priceTicks, order);
                _nextSequenceNumber++;
                order.Replenish(_nextSequenceNumber, time);
                _matcher.Working[order.Side].Add(priceTicks, order);

                return new UpdateOrderConfirmed(_security, time, order.CompanyId, order.ToOrder(),
                    order.ClientOrderId, previousExchangeOrderId, ToDecimal(priceTicks), 0);
            }

            return null;
        }

        private void Match(List<OrderBookEvent> events, long? auctionPriceTicks = null)
        {
            if (_status != OrderBookStatus.Open)
            {
                return;
            }

            var time = Now();
            var pendingImmediateOrCancelStops = new List<InternalOrder>();
            IMatchingAlgorithm algorithm = auctionPriceTicks.HasValue
                ? new Uncross(auctionPriceTicks.Value)
                : ContinuousMatch.Instance;

            foreach (var outcome in _matcher.Run(algorithm, CheckTradeRestrictionBreach))
                Apply(outcome, events, time, pendingImmediateOrCancelStops);

            // Deferred until the whole sweep is done, not checked right after each stop's own
            // conversion: since this loop only ever exits once no crosses remain anywhere in the
            // book, "did it fill" can't be answered any earlier than right here.
            foreach (var order in pendingImmediateOrCancelStops)
            {
                if (order.RemainingQuantity > 0)
                    events.Add(CancelRemainder(order, OrderCancelledReason.ImmediateOrCancelNotFilled));
            }
        }

        // On the first Trade-scoped restriction that disallows priceTicks, returns its OnBreach
        // consequence (Pause -> PreOpen, Halt -> Closed); a pure query, consulted by Matcher.Run
        // only outside an auction uncrossing pass.
        private RestrictionBreachAction? CheckTradeRestrictionBreach(long priceTicks)
        {
            foreach (var restriction in _priceRestrictions)
            {
                if (restriction.Scope == RestrictionScope.Trade && !restriction.Allows(priceTicks))
                    return restriction.OnBreach;
            }

            return null;
        }

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

                case TradeRestrictionBreached(_, var action):
                    _status = action == RestrictionBreachAction.Halt ? OrderBookStatus.Closed : OrderBookStatus.PreOpen;
                    events.Add(new StatusChanged(_security, Now(), _status));
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

            FillOrder(resting);
            var restingSnapshot = resting.ToOrder();
            var restingReplenish = FinishFill(resting, time);

            FillOrder(aggressor);
            var aggressorSnapshot = aggressor.ToOrder();
            var aggressorReplenish = FinishFill(aggressor, time);

            events.Add(new OrdersMatched(_security, time, price, quantity,
                new[]
                {
                    new FillOrderConfirmed(_security, time, resting.CompanyId, restingSnapshot, price, quantity,
                        true),
                    new FillOrderConfirmed(_security, time, aggressor.CompanyId, aggressorSnapshot, price,
                        quantity, false)
                }
            ));

            if (restingReplenish != null)
                events.Add(restingReplenish);
            if (aggressorReplenish != null)
                events.Add(aggressorReplenish);

            if (_lastTradedPrice != priceTicks)
            {
                _lastTradedPrice = priceTicks;
                _auctionReferencePriceTicks = priceTicks;
                foreach (var restriction in _priceRestrictions)
                    restriction.OnTrade(priceTicks, time);
            }
        }

        private void TriggerStops(IReadOnlyList<InternalOrder> orders, DateTime time, List<OrderBookEvent> events,
            List<InternalOrder> pendingImmediateOrCancelStops)
        {
            foreach (var order in orders)
            {
                var triggerPriceTicks = order.TriggerPrice ??
                    throw new InvalidOperationException("stop order missing stop price");
                _matcher.Stops[order.Side].Remove(triggerPriceTicks, order);

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

                    // still Hidden here (never converted to a working limit order), so there's
                    // no working-book level to remove it from.
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

                var limitPriceTicks = order.Price ?? throw new Exception("missing price");
                _matcher.Working[order.Side].Add(limitPriceTicks, order);

                // previousPrice null - the order was resting in the stops ladder, not the
                // working book, so this is an arrival, not a move between working-book levels.
                events.Add(new UpdateOrderConfirmed(_security, time, order.CompanyId, order.ToOrder(),
                    order.ClientOrderId, previousExchangeOrderId, null, order.DisplayedQuantity));
            }
        }

        private List<OrderBookEvent> UpdateStatus(OrderBookStatus status, decimal? referencePrice = null)
        {
            if (referencePrice.HasValue && TryConvertToTicks(referencePrice, out var referenceTicks))
            {
                _auctionReferencePriceTicks = referenceTicks;
                foreach (var restriction in _priceRestrictions)
                    restriction.OnSessionChange(referenceTicks);
            }

            return status switch
            {
                OrderBookStatus.PreOpen => PreOpenMarket(),
                OrderBookStatus.Open => OpenMarket(),
                OrderBookStatus.Closed => CloseMarket(),
                _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
            };
        }

        private List<OrderBookEvent> PreOpenMarket()
        {
            // TODO: need better system for multiple sessions per day
            var date = Now();
            _nextSequenceNumber = ((date.Year * 10000) + (date.Month * 100) + date.Day) * 10000000000L;
            _status = OrderBookStatus.PreOpen;
            return new List<OrderBookEvent> {new StatusChanged(_security, Now(), _status)};
        }

        private List<OrderBookEvent> OpenMarket()
        {
            _status = OrderBookStatus.Open;
            var events = new List<OrderBookEvent> {new StatusChanged(_security, Now(), _status)};

            // Runs whether the prior status was the real start-of-day PreOpen or PreOpen
            // re-entered mid-session for a volatility pause - same uncrossing either way.
            if (_matcher.TryComputeAuctionPrice(_auctionReferencePriceTicks, out var auctionPriceTicks, out _))
                Match(events, auctionPriceTicks);

            Match(events);
            return events;
        }

        private List<OrderBookEvent> CloseMarket()
        {
            _status = OrderBookStatus.Closed;
            var events = new List<OrderBookEvent> {new StatusChanged(_security, Now(), _status)};
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

    public record Level(decimal Price, int Quantity, int Count);
}
