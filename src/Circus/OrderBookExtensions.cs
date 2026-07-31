using Circus.Actions;
using Circus.Events;

namespace Circus;

// Convenience over Process(action) for callers building one action at a time.
//
// Each takes an optional time. Left out, the action goes unstamped and wants a book that stamps
// - TimestampingOrderBook, or anything else wrapping one; a bare OrderBook refuses it and says
// so, rather than treating the missing stamp as the start of time. Passed, it is the instant the
// action happened, which is what a caller owning a schedule should do: a session provider knows
// the boundary time it is firing on, and that is more truthful than whatever a clock reads by
// the time the event handler runs.
public static class OrderBookExtensions
{
    private static SelfMatchPrevention? BuildSelfMatchPrevention(string? id,
        SelfMatchPreventionInstruction? instruction) =>
        id == null ? null : new SelfMatchPrevention { Id = id, Instruction = instruction };

    public static IReadOnlyList<OrderBookEvent> CreateLimitOrder(this IOrderBook book, string companyId,
        string clientOrderId, OrderValidity orderValidity, Side side, int quantity, decimal price,
        string? selfMatchPreventionId = null,
        SelfMatchPreventionInstruction? selfMatchPreventionInstruction = null,
        int? maxVisibleQuantity = null, DateTime time = default) =>
        book.Process(new CreateLimitOrder
        {
            Symbol = book.Symbol, Time = time, CompanyId = companyId, ClientOrderId = clientOrderId,
            OrderValidity = orderValidity, Side = side, Quantity = quantity, Price = price,
            SelfMatchPrevention = BuildSelfMatchPrevention(selfMatchPreventionId, selfMatchPreventionInstruction),
            MaxVisibleQuantity = maxVisibleQuantity
        });

    public static IReadOnlyList<OrderBookEvent> CreateMarketOrder(this IOrderBook book, string companyId,
        string clientOrderId, OrderValidity orderValidity, Side side, int quantity,
        string? selfMatchPreventionId = null,
        SelfMatchPreventionInstruction? selfMatchPreventionInstruction = null,
        int? maxVisibleQuantity = null, DateTime time = default) =>
        book.Process(new CreateMarketOrder
        {
            Symbol = book.Symbol, Time = time, CompanyId = companyId, ClientOrderId = clientOrderId,
            OrderValidity = orderValidity, Side = side, Quantity = quantity,
            SelfMatchPrevention = BuildSelfMatchPrevention(selfMatchPreventionId, selfMatchPreventionInstruction),
            MaxVisibleQuantity = maxVisibleQuantity
        });

    public static IReadOnlyList<OrderBookEvent> CreateMarketLimitOrder(this IOrderBook book, string companyId,
        string clientOrderId, OrderValidity orderValidity, Side side, int quantity,
        string? selfMatchPreventionId = null,
        SelfMatchPreventionInstruction? selfMatchPreventionInstruction = null,
        int? maxVisibleQuantity = null, DateTime time = default) =>
        book.Process(new CreateMarketLimitOrder
        {
            Symbol = book.Symbol, Time = time, CompanyId = companyId, ClientOrderId = clientOrderId,
            OrderValidity = orderValidity, Side = side, Quantity = quantity,
            SelfMatchPrevention = BuildSelfMatchPrevention(selfMatchPreventionId, selfMatchPreventionInstruction),
            MaxVisibleQuantity = maxVisibleQuantity
        });

    public static IReadOnlyList<OrderBookEvent> CreateStopLimitOrder(this IOrderBook book, string companyId,
        string clientOrderId, OrderValidity orderValidity, Side side, int quantity, decimal price,
        decimal triggerPrice, string? selfMatchPreventionId = null,
        SelfMatchPreventionInstruction? selfMatchPreventionInstruction = null,
        int? maxVisibleQuantity = null, DateTime time = default) =>
        book.Process(new CreateStopLimitOrder
        {
            Symbol = book.Symbol, Time = time, CompanyId = companyId, ClientOrderId = clientOrderId,
            OrderValidity = orderValidity, Side = side, Quantity = quantity,
            Price = price, TriggerPrice = triggerPrice,
            SelfMatchPrevention = BuildSelfMatchPrevention(selfMatchPreventionId, selfMatchPreventionInstruction),
            MaxVisibleQuantity = maxVisibleQuantity
        });

    public static IReadOnlyList<OrderBookEvent> CreateStopMarketOrder(this IOrderBook book, string companyId,
        string clientOrderId, OrderValidity orderValidity, Side side, int quantity, decimal triggerPrice,
        string? selfMatchPreventionId = null,
        SelfMatchPreventionInstruction? selfMatchPreventionInstruction = null,
        int? maxVisibleQuantity = null, DateTime time = default) =>
        book.Process(new CreateStopMarketOrder
        {
            Symbol = book.Symbol, Time = time, CompanyId = companyId, ClientOrderId = clientOrderId,
            OrderValidity = orderValidity, Side = side, Quantity = quantity,
            TriggerPrice = triggerPrice,
            SelfMatchPrevention = BuildSelfMatchPrevention(selfMatchPreventionId, selfMatchPreventionInstruction),
            MaxVisibleQuantity = maxVisibleQuantity
        });

    public static IReadOnlyList<OrderBookEvent> UpdateOrder(this IOrderBook book, string companyId,
        string clientOrderId, string previousClientOrderId, int? newTotalQuantity = null, decimal? price = null,
        decimal? triggerPrice = null, DateTime time = default) =>
        book.Process(new UpdateOrder
        {
            Symbol = book.Symbol, Time = time, CompanyId = companyId, ClientOrderId = clientOrderId,
            PreviousClientOrderId = previousClientOrderId, NewTotalQuantity = newTotalQuantity, Price = price,
            TriggerPrice = triggerPrice
        });

    public static IReadOnlyList<OrderBookEvent> CancelOrder(this IOrderBook book, string companyId,
        string clientOrderId, string previousClientOrderId, DateTime time = default) =>
        book.Process(new CancelOrder
        {
            Symbol = book.Symbol, Time = time, CompanyId = companyId, ClientOrderId = clientOrderId,
            PreviousClientOrderId = previousClientOrderId
        });

    public static IReadOnlyList<OrderBookEvent> PreOpenTrading(this IOrderBook book,
        decimal? referencePrice = null, DateTime time = default) =>
        book.Process(new PreOpenTrading
            { Symbol = book.Symbol, Time = time, ReferencePrice = referencePrice });

    public static IReadOnlyList<OrderBookEvent> OpenTrading(this IOrderBook book,
        decimal? referencePrice = null, DateTime time = default) =>
        book.Process(new OpenTrading
            { Symbol = book.Symbol, Time = time, ReferencePrice = referencePrice });

    public static IReadOnlyList<OrderBookEvent> CloseTrading(this IOrderBook book, bool endsTradingDay = true,
        DateTime time = default) =>
        book.Process(new CloseTrading
            { Symbol = book.Symbol, Time = time, EndsTradingDay = endsTradingDay });

    public static IReadOnlyList<OrderBookEvent> PauseTrading(this IOrderBook book, DateTime time = default) =>
        book.Process(new PauseTrading { Symbol = book.Symbol, Time = time });

    public static IReadOnlyList<OrderBookEvent> HaltTrading(this IOrderBook book, DateTime time = default) =>
        book.Process(new HaltTrading { Symbol = book.Symbol, Time = time });

    // Returns whatever the elapsed time turned out to imply - a resumption and its print, or
    // nothing at all.
    public static IReadOnlyList<OrderBookEvent> AdvanceTime(this IOrderBook book, DateTime time = default) =>
        book.Process(new AdvanceTime { Symbol = book.Symbol, Time = time });

    // Bridge for callers that only know the target status at runtime (e.g. a session/schedule
    // provider driving the book off a clock) and so can't pick PreOpenTrading/OpenTrading/
    // CloseTrading directly. ReferencePrice is ignored for every status but the two opening
    // ones - a reference price is meaningless once the book stops trading - and endsTradingDay
    // applies only to closing, since nothing else ends a day.
    public static IReadOnlyList<OrderBookEvent> UpdateStatus(this IOrderBook book, OrderBookStatus status,
        decimal? referencePrice = null, bool endsTradingDay = true, DateTime time = default) =>
        status switch
        {
            OrderBookStatus.PreOpen => book.PreOpenTrading(referencePrice, time),
            OrderBookStatus.Open => book.OpenTrading(referencePrice, time),
            OrderBookStatus.Closed => book.CloseTrading(endsTradingDay, time),
            OrderBookStatus.Paused => book.PauseTrading(time),
            OrderBookStatus.Halted => book.HaltTrading(time),
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };
}
