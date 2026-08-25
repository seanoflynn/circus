namespace Circus.Actions;

public abstract record OrderBookAction
{
    public required string Symbol { get; init; }

    public DateTime Time { get; init; }
}

public abstract record SessionAction : OrderBookAction
{
    public DateOnly? TradeDate { get; init; }
}

public sealed record PreOpenTrading : SessionAction
{
    public decimal? ReferencePrice { get; init; }
}

public sealed record OpenTrading : SessionAction
{
    public decimal? ReferencePrice { get; init; }
}

public sealed record CloseTrading : SessionAction
{
    public bool EndsTradingDay { get; init; } = true;
}

public sealed record PauseTrading : OrderBookAction;

public sealed record HaltTrading : OrderBookAction;

public sealed record AdvanceTime : OrderBookAction;

public sealed record PublishSnapshot : OrderBookAction;

public abstract record OrderAction : OrderBookAction
{
    public required string CompanyId { get; init; }
    public required string ClientOrderId { get; init; }
}

public abstract record CreateOrder : OrderAction
{
    public required OrderValidity OrderValidity { get; init; }
    public required Side Side { get; init; }
    public required int Quantity { get; init; }
    public SelfMatchPrevention? SelfMatchPrevention { get; init; }

    public int? MaxVisibleQuantity { get; init; }
}

public sealed record CreateLimitOrder : CreateOrder
{
    public required decimal Price { get; init; }
}

public sealed record CreateMarketOrder : CreateOrder;

public sealed record CreateMarketLimitOrder : CreateOrder;

public sealed record CreateStopLimitOrder : CreateOrder
{
    public required decimal Price { get; init; }
    public required decimal TriggerPrice { get; init; }
}

public sealed record CreateStopMarketOrder : CreateOrder
{
    public required decimal TriggerPrice { get; init; }
}

public sealed record UpdateOrder : OrderAction
{
    public required string PreviousClientOrderId { get; init; }
    public int? NewTotalQuantity { get; init; }
    public decimal? Price { get; init; }
    public decimal? TriggerPrice { get; init; }
}

public sealed record CancelOrder : OrderAction
{
    public required string PreviousClientOrderId { get; init; }
}