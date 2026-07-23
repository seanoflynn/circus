using System;

namespace Circus.OrderBook
{
    // Price/TriggerPrice are stored as tick counts (price / Security.TickSize), not decimal.
    // decimal division/comparison has no hardware path, so keeping the hot comparison and
    // dictionary-key paths on long avoids that cost; conversion back to decimal only happens
    // at the public-API boundary (ToOrder/ToString).
    internal class InternalOrder
    {
        public long SequenceNumber { get; private set; }
        public string CompanyId { get; }
        public string ExchangeOrderId { get; }
        public string ClientOrderId { get; private set; }
        public Security Security { get; }
        public DateTime CreatedTime { get; }
        public DateTime ModifiedTime { get; private set; }
        public DateTime? CompletedTime { get; private set; }
        public OrderStatus Status { get; private set; }
        public OrderType Type { get; private set; }
        public OrderValidity Validity { get; }
        public Side Side { get; }
        public int Quantity { get; private set; }
        public int RemainingQuantity { get; private set; }
        public int FilledQuantity { get; private set; }
        public long? Price { get; private set; }
        public long? TriggerPrice { get; private set; }
        public DateOnly? GoodTilDate { get; }
        public string? SelfMatchPreventionId { get; }
        public SelfMatchPreventionInstruction? SelfMatchPreventionInstruction { get; }

        public InternalOrder(long sequenceNumber, string companyId, string clientOrderId, Security security, DateTime time,
            OrderStatus status, OrderType type, OrderValidity validity, Side side, int quantity, long? price,
            long? triggerPrice, DateOnly? goodTilDate = null, string? selfMatchPreventionId = null,
            SelfMatchPreventionInstruction? selfMatchPreventionInstruction = null)
        {
            SequenceNumber = sequenceNumber;
            CompanyId = companyId;
            ExchangeOrderId = sequenceNumber.ToString();
            ClientOrderId = clientOrderId;
            Security = security;
            CreatedTime = time;
            ModifiedTime = time;
            Status = status;
            Type = type;
            Validity = validity;
            Side = side;
            Quantity = quantity;
            RemainingQuantity = Quantity;
            FilledQuantity = 0;
            Price = price;
            TriggerPrice = triggerPrice;
            GoodTilDate = goodTilDate;
            SelfMatchPreventionId = selfMatchPreventionId;
            SelfMatchPreventionInstruction = selfMatchPreventionInstruction;
        }

        public override string ToString() =>
            $"[Order #{ExchangeOrderId} company={CompanyId} clientOrder={ClientOrderId} {Status} {ModifiedTime:HH:mm:ss} {Side} {Quantity}@{ToDecimal(Price)}]";

        public Order ToOrder()
        {
            return new(CompanyId, ExchangeOrderId, ClientOrderId, Security, CreatedTime, ModifiedTime, CompletedTime,
                Status, Type, Validity, Side, Quantity, FilledQuantity, RemainingQuantity, ToDecimal(Price),
                ToDecimal(TriggerPrice), GoodTilDate, SelfMatchPreventionId, SelfMatchPreventionInstruction);
        }

        private decimal? ToDecimal(long? ticks) => ticks.HasValue ? ticks.Value * Security.TickSize : null;

        public void Cancel(DateTime time, string? clientOrderId = null)
        {
            if (clientOrderId != null)
            {
                ClientOrderId = clientOrderId;
            }

            RemainingQuantity = 0;
            CompletedTime = time;
            Status = OrderStatus.Cancelled;
        }

        public void Expire(DateTime time)
        {
            RemainingQuantity = 0;
            CompletedTime = time;
            Status = OrderStatus.Expired;
        }

        public void Update(long sequenceNumber, DateTime time, int? quantity, long? triggerPrice, long? price,
            string clientOrderId)
        {
            SequenceNumber = sequenceNumber;
            ModifiedTime = time;
            ClientOrderId = clientOrderId;
            if (quantity.HasValue)
            {
                // quantity is the new total size, so remaining = new total - already filled
                RemainingQuantity -= (Quantity - quantity.Value);
                Quantity = quantity.Value;
            }

            if (triggerPrice.HasValue)
            {
                TriggerPrice = triggerPrice;
            }

            if (price.HasValue)
            {
                Price = price;
            }
        }

        public void Fill(DateTime time, int quantity)
        {
            // TODO: validate quantity

            FilledQuantity += quantity;
            RemainingQuantity -= quantity;

            if (RemainingQuantity == 0)
            {
                Status = OrderStatus.Filled;
                CompletedTime = time;
            }
        }

        public void ConvertToLimit(DateTime time, long sequenceNumber, long? price = null)
        {
            if (price.HasValue)
            {
                Price = price;
            }

            SequenceNumber = sequenceNumber;
            ModifiedTime = time;
            Type = OrderType.Limit;
            Status = OrderStatus.Working;
        }
    }
}
