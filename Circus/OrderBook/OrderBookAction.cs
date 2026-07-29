namespace Circus.OrderBook
{
    public abstract record OrderBookAction
    {
        public required Security Security { get; init; }
    }

    public sealed record PreOpenTrading : OrderBookAction
    {
        public decimal? ReferencePrice { get; init; }
    }

    public sealed record OpenTrading : OrderBookAction
    {
        public decimal? ReferencePrice { get; init; }
    }

    // A trading day can hold several sessions, and only the last close of the day retires that
    // day's Day/GoodTilDate orders - an intra-day close (a lunch break, say) leaves them resting.
    // Defaults to true so a single-session day needs to say nothing.
    public sealed record CloseTrading : OrderBookAction
    {
        public bool EndsTradingDay { get; init; } = true;
    }

    public abstract record OrderAction : OrderBookAction
    {
        public required string CompanyId { get; init; }
        public required string ClientOrderId { get; init; }
    }

    // Id is required: an instruction with no id is meaningless (nothing to match against), so
    // rather than let that combination be constructed and silently ignored, opting into self-match
    // prevention at all means supplying an id - Instruction is the only genuinely optional part,
    // falling back to CancelResting when omitted.
    public sealed record SelfMatchPrevention
    {
        public required string Id { get; init; }
        public SelfMatchPreventionInstruction? Instruction { get; init; }
    }

    public abstract record CreateOrder : OrderAction
    {
        public required OrderValidity OrderValidity { get; init; }
        public required Side Side { get; init; }
        public required int Quantity { get; init; }
        public SelfMatchPrevention? SelfMatchPrevention { get; init; }

        // Iceberg/display quantity - the portion shown to the market at a time, with the rest
        // held in reserve. Not restricted to CreateLimitOrder: Market/MarketLimit/triggered-Stop
        // orders can all end up resting as a plain limit order too, so display quantity is
        // meaningful for any of them.
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
}
