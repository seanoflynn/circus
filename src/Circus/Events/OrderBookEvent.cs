using Circus.MarketData;

namespace Circus.Events;

public record OrderBookEvent(string Symbol, DateTime Time);

public abstract record MarketEvent(string Symbol, DateTime Time) : OrderBookEvent(Symbol, Time);

public record StatusChanged(string Symbol, DateTime Time, OrderBookStatus Status,
        OrderBookStatusChangeReason Reason = OrderBookStatusChangeReason.Requested, DateTime? ResumesAt = null,
        Side? LimitState = null)
    : MarketEvent(Symbol, Time);

public record OrderEvent(string Symbol, DateTime Time, string CompanyId, string ClientOrderId,
        string? ExchangeOrderId)
    : OrderBookEvent(Symbol, Time);

public record OrderConfirmedEvent(string Symbol, DateTime Time, string CompanyId, Order Order)
    : OrderEvent(Symbol, Time, CompanyId, Order.ClientOrderId, Order.ExchangeOrderId);

public record CreateOrderConfirmed(string Symbol, DateTime Time, string CompanyId, Order Order)
    : OrderConfirmedEvent(Symbol, Time, CompanyId, Order);

public record UpdateOrderConfirmed(string Symbol, DateTime Time, string CompanyId, Order Order,
        string PreviousClientOrderId, string PreviousExchangeOrderId, decimal? PreviousPrice, int PreviousQuantity)
    : OrderConfirmedEvent(Symbol, Time, CompanyId, Order);

public record CancelOrderConfirmed(string Symbol, DateTime Time, string CompanyId, Order Order,
        string PreviousClientOrderId, OrderCancelledReason Reason, decimal? PreviousPrice, int PreviousQuantity)
    : OrderConfirmedEvent(Symbol, Time, CompanyId, Order);

public record ExpireOrderConfirmed(string Symbol, DateTime Time, string CompanyId, Order Order,
        decimal? PreviousPrice, int PreviousQuantity)
    : OrderConfirmedEvent(Symbol, Time, CompanyId, Order);

public record FillOrderConfirmed(string Symbol, DateTime Time, string CompanyId, Order Order, string TradeId,
        decimal Price, int Quantity, int PreviousDisplayedQuantity, bool IsResting)
    : OrderConfirmedEvent(Symbol, Time, CompanyId, Order);

public record OrderRejectedEvent(string Symbol, DateTime Time, string CompanyId, string ClientOrderId,
        string? ExchangeOrderId, OrderRejectedReason Reason)
    : OrderEvent(Symbol, Time, CompanyId, ClientOrderId, ExchangeOrderId);

public record CreateOrderRejected(string Symbol, DateTime Time, string CompanyId, string ClientOrderId,
        OrderRejectedReason Reason)
    : OrderRejectedEvent(Symbol, Time, CompanyId, ClientOrderId, null, Reason);

public record UpdateOrderRejected(string Symbol, DateTime Time, string CompanyId, string ClientOrderId,
        string PreviousClientOrderId, string? ExchangeOrderId, OrderRejectedReason Reason)
    : OrderRejectedEvent(Symbol, Time, CompanyId, ClientOrderId, ExchangeOrderId, Reason);

public record CancelOrderRejected(string Symbol, DateTime Time, string CompanyId, string ClientOrderId,
        string PreviousClientOrderId, string? ExchangeOrderId, OrderRejectedReason Reason)
    : OrderRejectedEvent(Symbol, Time, CompanyId, ClientOrderId, ExchangeOrderId, Reason);

public record IndicativePriceChanged(string Symbol, DateTime Time, decimal? Price, int Quantity)
    : MarketEvent(Symbol, Time);

public record LimitStateChanged(string Symbol, DateTime Time, Side? Side, decimal? Price,
        OrderBookStatus Status, OrderBookStatusChangeReason Reason, DateTime? ResumesAt)
    : MarketEvent(Symbol, Time);
public record LevelChange(Side Side, int LevelIndex, decimal Price, int Quantity, int Count,
    LevelChangeAction Action);

public record LevelsChanged(string Symbol, DateTime Time, int Depth,
        IReadOnlyList<LevelChange> Changes)
    : MarketEvent(Symbol, Time)
{
    // Spelled out because a record's generated equality compares a list member by reference, and
    // DeterminismTests asserts that a replay reproduces every event by value.
    public virtual bool Equals(LevelsChanged? other) =>
        other is not null
        && EqualityContract == other.EqualityContract
        && Symbol == other.Symbol
        && Time == other.Time
        && Depth == other.Depth
        && Changes.SequenceEqual(other.Changes);

    public override int GetHashCode() => HashCode.Combine(Symbol, Time, Depth, Changes.Count);

    public override string ToString() =>
        $"{nameof(LevelsChanged)} {{ Symbol = {Symbol}, Time = {Time:O}, Depth = {Depth}, " +
        $"Changes = [{string.Join(", ", Changes)}] }}";
}

public record BookSnapshot(string Symbol, DateTime Time, IReadOnlyList<Level> Bids,
        IReadOnlyList<Level> Offers, IReadOnlyList<RestingOrder> Orders, OrderBookStatus Status,
        OrderBookStatusChangeReason StatusReason, DateTime? ResumesAt, Side? LimitState,
        decimal? IndicativePrice, int IndicativeQuantity)
    : MarketEvent(Symbol, Time)
{
    // Spelled out for the reason LevelsChanged spells them out.
    public virtual bool Equals(BookSnapshot? other) =>
        other is not null
        && EqualityContract == other.EqualityContract
        && Symbol == other.Symbol
        && Time == other.Time
        && Status == other.Status
        && StatusReason == other.StatusReason
        && ResumesAt == other.ResumesAt
        && LimitState == other.LimitState
        && IndicativePrice == other.IndicativePrice
        && IndicativeQuantity == other.IndicativeQuantity
        && Bids.SequenceEqual(other.Bids)
        && Offers.SequenceEqual(other.Offers)
        && Orders.SequenceEqual(other.Orders);

    public override int GetHashCode() =>
        HashCode.Combine(Symbol, Time, Status, Bids.Count, Offers.Count, IndicativePrice);

    public override string ToString() =>
        $"{nameof(BookSnapshot)} {{ Symbol = {Symbol}, Time = {Time:O}, Status = {Status}, " +
        $"Bids = [{string.Join(", ", Bids)}], Offers = [{string.Join(", ", Offers)}] }}";
}

public record OrderChange(Side Side, string ExchangeOrderId, decimal Price, int Quantity,
    OrderChangeAction Action, string? TradeId = null);

public record OrdersChanged(string Symbol, DateTime Time, IReadOnlyList<OrderChange> Changes)
    : MarketEvent(Symbol, Time)
{
    public virtual bool Equals(OrdersChanged? other) =>
        other is not null
        && EqualityContract == other.EqualityContract
        && Symbol == other.Symbol
        && Time == other.Time
        && Changes.SequenceEqual(other.Changes);

    public override int GetHashCode() => HashCode.Combine(Symbol, Time, Changes.Count);

    public override string ToString() =>
        $"{nameof(OrdersChanged)} {{ Symbol = {Symbol}, Time = {Time:O}, " +
        $"Changes = [{string.Join(", ", Changes)}] }}";
}

public record TradePrinted(string Symbol, DateTime Time, string TradeId, decimal Price, int Quantity)
    : MarketEvent(Symbol, Time);
