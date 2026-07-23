using System;
using System.Collections.Generic;

namespace Circus.OrderBook
{
    public interface IOrderBook
    {
        Security Security { get; }

        OrderBookStatus Status { get; }

        IList<Level> GetLevels(Side side, int maxPrices);

        // Live indicative auction price during PreOpen (or a mid-session volatility pause) - the
        // price/quantity that would result if the auction ended right now. Recalculated from the
        // current book on every call, so it naturally tracks order entry/cancellation.
        bool TryGetIndicativeAuctionPrice(out decimal price, out int quantity);

        IList<OrderBookEvent> Process(OrderBookAction action);

        IList<OrderBookEvent> CreateOrder(string companyId, string clientOrderId, OrderValidity validity, Side side,
            int quantity, decimal? price = null, decimal? triggerPrice = null, bool marketLimit = false,
            DateOnly? goodTilDate = null, string? selfMatchPreventionId = null,
            SelfMatchPreventionInstruction? selfMatchPreventionInstruction = null);

        // quantity is the order's new total size (filled + remaining), not the new remaining/resting amount
        IList<OrderBookEvent> UpdateOrder(string companyId, string clientOrderId, string previousClientOrderId,
            int? quantity = null, decimal? price = null, decimal? triggerPrice = null);

        IList<OrderBookEvent> CancelOrder(string companyId, string clientOrderId, string previousClientOrderId);

        IList<OrderBookEvent> UpdateStatus(OrderBookStatus status, decimal? referencePrice = null);
    }
}