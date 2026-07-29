using System;
using System.Collections.Generic;

namespace Circus.OrderBook
{
    public static class OrderBookExtensions
    {
        private static SelfMatchPrevention? BuildSelfMatchPrevention(string? id,
            SelfMatchPreventionInstruction? instruction) =>
            id == null ? null : new SelfMatchPrevention { Id = id, Instruction = instruction };

        public static IReadOnlyList<OrderBookEvent> CreateLimitOrder(this IOrderBook book, string companyId,
            string clientOrderId, OrderValidity orderValidity, Side side, int quantity, decimal price,
            string? selfMatchPreventionId = null,
            SelfMatchPreventionInstruction? selfMatchPreventionInstruction = null,
            int? maxVisibleQuantity = null) =>
            book.Process(new CreateLimitOrder
            {
                Security = book.Security, CompanyId = companyId, ClientOrderId = clientOrderId,
                OrderValidity = orderValidity, Side = side, Quantity = quantity, Price = price,
                SelfMatchPrevention = BuildSelfMatchPrevention(selfMatchPreventionId, selfMatchPreventionInstruction),
                MaxVisibleQuantity = maxVisibleQuantity
            });

        public static IReadOnlyList<OrderBookEvent> CreateMarketOrder(this IOrderBook book, string companyId,
            string clientOrderId, OrderValidity orderValidity, Side side, int quantity,
            string? selfMatchPreventionId = null,
            SelfMatchPreventionInstruction? selfMatchPreventionInstruction = null,
            int? maxVisibleQuantity = null) =>
            book.Process(new CreateMarketOrder
            {
                Security = book.Security, CompanyId = companyId, ClientOrderId = clientOrderId,
                OrderValidity = orderValidity, Side = side, Quantity = quantity,
                SelfMatchPrevention = BuildSelfMatchPrevention(selfMatchPreventionId, selfMatchPreventionInstruction),
                MaxVisibleQuantity = maxVisibleQuantity
            });

        public static IReadOnlyList<OrderBookEvent> CreateMarketLimitOrder(this IOrderBook book, string companyId,
            string clientOrderId, OrderValidity orderValidity, Side side, int quantity,
            string? selfMatchPreventionId = null,
            SelfMatchPreventionInstruction? selfMatchPreventionInstruction = null,
            int? maxVisibleQuantity = null) =>
            book.Process(new CreateMarketLimitOrder
            {
                Security = book.Security, CompanyId = companyId, ClientOrderId = clientOrderId,
                OrderValidity = orderValidity, Side = side, Quantity = quantity,
                SelfMatchPrevention = BuildSelfMatchPrevention(selfMatchPreventionId, selfMatchPreventionInstruction),
                MaxVisibleQuantity = maxVisibleQuantity
            });

        public static IReadOnlyList<OrderBookEvent> CreateStopLimitOrder(this IOrderBook book, string companyId,
            string clientOrderId, OrderValidity orderValidity, Side side, int quantity, decimal price,
            decimal triggerPrice, string? selfMatchPreventionId = null,
            SelfMatchPreventionInstruction? selfMatchPreventionInstruction = null,
            int? maxVisibleQuantity = null) =>
            book.Process(new CreateStopLimitOrder
            {
                Security = book.Security, CompanyId = companyId, ClientOrderId = clientOrderId,
                OrderValidity = orderValidity, Side = side, Quantity = quantity,
                Price = price, TriggerPrice = triggerPrice,
                SelfMatchPrevention = BuildSelfMatchPrevention(selfMatchPreventionId, selfMatchPreventionInstruction),
                MaxVisibleQuantity = maxVisibleQuantity
            });

        public static IReadOnlyList<OrderBookEvent> CreateStopMarketOrder(this IOrderBook book, string companyId,
            string clientOrderId, OrderValidity orderValidity, Side side, int quantity, decimal triggerPrice,
            string? selfMatchPreventionId = null,
            SelfMatchPreventionInstruction? selfMatchPreventionInstruction = null,
            int? maxVisibleQuantity = null) =>
            book.Process(new CreateStopMarketOrder
            {
                Security = book.Security, CompanyId = companyId, ClientOrderId = clientOrderId,
                OrderValidity = orderValidity, Side = side, Quantity = quantity,
                TriggerPrice = triggerPrice,
                SelfMatchPrevention = BuildSelfMatchPrevention(selfMatchPreventionId, selfMatchPreventionInstruction),
                MaxVisibleQuantity = maxVisibleQuantity
            });

        public static IReadOnlyList<OrderBookEvent> UpdateOrder(this IOrderBook book, string companyId,
            string clientOrderId, string previousClientOrderId, int? newTotalQuantity = null, decimal? price = null,
            decimal? triggerPrice = null) =>
            book.Process(new UpdateOrder
            {
                Security = book.Security, CompanyId = companyId, ClientOrderId = clientOrderId,
                PreviousClientOrderId = previousClientOrderId, NewTotalQuantity = newTotalQuantity, Price = price,
                TriggerPrice = triggerPrice
            });

        public static IReadOnlyList<OrderBookEvent> CancelOrder(this IOrderBook book, string companyId,
            string clientOrderId, string previousClientOrderId) =>
            book.Process(new CancelOrder
            {
                Security = book.Security, CompanyId = companyId, ClientOrderId = clientOrderId,
                PreviousClientOrderId = previousClientOrderId
            });

        public static IReadOnlyList<OrderBookEvent> PreOpenTrading(this IOrderBook book,
            decimal? referencePrice = null) =>
            book.Process(new PreOpenTrading { Security = book.Security, ReferencePrice = referencePrice });

        public static IReadOnlyList<OrderBookEvent> OpenTrading(this IOrderBook book,
            decimal? referencePrice = null) =>
            book.Process(new OpenTrading { Security = book.Security, ReferencePrice = referencePrice });

        public static IReadOnlyList<OrderBookEvent> CloseTrading(this IOrderBook book, bool endsTradingDay = true) =>
            book.Process(new CloseTrading { Security = book.Security, EndsTradingDay = endsTradingDay });

        public static IReadOnlyList<OrderBookEvent> PauseTrading(this IOrderBook book) =>
            book.Process(new PauseTrading { Security = book.Security });

        public static IReadOnlyList<OrderBookEvent> HaltTrading(this IOrderBook book) =>
            book.Process(new HaltTrading { Security = book.Security });

        // Bridge for callers that only know the target status at runtime (e.g. a session/schedule
        // provider driving the book off a clock) and so can't pick PreOpenTrading/OpenTrading/
        // CloseTrading directly. ReferencePrice is ignored for every status but the two opening
        // ones - a reference price is meaningless once the book stops trading - and endsTradingDay
        // applies only to closing, since nothing else ends a day.
        public static IReadOnlyList<OrderBookEvent> UpdateStatus(this IOrderBook book, OrderBookStatus status,
            decimal? referencePrice = null, bool endsTradingDay = true) =>
            status switch
            {
                OrderBookStatus.PreOpen => book.PreOpenTrading(referencePrice),
                OrderBookStatus.Open => book.OpenTrading(referencePrice),
                OrderBookStatus.Closed => book.CloseTrading(endsTradingDay),
                OrderBookStatus.Paused => book.PauseTrading(),
                OrderBookStatus.Halted => book.HaltTrading(),
                _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
            };
    }
}
