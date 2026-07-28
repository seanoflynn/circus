using System;

namespace Circus.OrderBook
{
    // Price/TriggerPrice are tick counts (price / Security.TickSize), not decimal: decimal
    // comparison has no hardware path, so the hot paths stay on long and convert back only at the
    // public-API boundary.
    internal class InternalOrder
    {
        public long SequenceNumber { get; private set; }

        // Stable for the order's whole life and never exposed. ExchangeOrderId is the public
        // identity, and unlike this one it deliberately does change.
        public long InternalId { get; }

        public string CompanyId { get; }

        // Derived from SequenceNumber so it cannot drift: every priority-losing mutation bumps
        // that and so changes this. Real exchanges do the same, since an order sent to the back of
        // the queue is functionally a new entry.
        public string ExchangeOrderId => SequenceNumber.ToString();

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
        public string? SelfMatchPreventionId { get; }
        public SelfMatchPreventionInstruction? SelfMatchPreventionInstruction { get; }
        public int? MaxVisibleQuantity { get; }

        // The shown portion of an iceberg, against RemainingQuantity's true total. Equal to it for
        // a non-iceberg order, so nothing elsewhere special-cases that.
        public int DisplayedQuantity { get; private set; }

        // Intrusive list pointers for the level currently holding this order - an order rests in
        // only one at a time. Avoids a node object per order.
        internal InternalOrder? LevelNext { get; set; }
        internal InternalOrder? LevelPrev { get; set; }

        public InternalOrder(long sequenceNumber, string companyId, string clientOrderId, Security security, DateTime time,
            OrderStatus status, OrderType type, OrderValidity validity, Side side, int quantity, long? price,
            long? triggerPrice, string? selfMatchPreventionId = null,
            SelfMatchPreventionInstruction? selfMatchPreventionInstruction = null, int? maxVisibleQuantity = null)
        {
            SequenceNumber = sequenceNumber;
            InternalId = sequenceNumber;
            CompanyId = companyId;
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
            SelfMatchPreventionId = selfMatchPreventionId;
            SelfMatchPreventionInstruction = selfMatchPreventionInstruction;
            MaxVisibleQuantity = maxVisibleQuantity;
            DisplayedQuantity = Math.Min(maxVisibleQuantity ?? quantity, quantity);
        }

        public override string ToString() =>
            $"[Order #{ExchangeOrderId} company={CompanyId} clientOrder={ClientOrderId} {Status} {ModifiedTime:HH:mm:ss} {Side} {Quantity}@{ToDecimal(Price)}]";

        public Order ToOrder()
        {
            return new(CompanyId, ExchangeOrderId, ClientOrderId, Security, CreatedTime, ModifiedTime, CompletedTime,
                Status, Type, Validity, Side, Quantity, FilledQuantity, RemainingQuantity, DisplayedQuantity,
                ToDecimal(Price), ToDecimal(TriggerPrice), SelfMatchPreventionId, SelfMatchPreventionInstruction,
                MaxVisibleQuantity);
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

                // keep displayed in sync - for a non-iceberg order this just mirrors
                // RemainingQuantity (no peak to cap it below); for an iceberg it's re-derived the
                // same way construction does, capped to whatever peak is still configured
                DisplayedQuantity = Math.Min(MaxVisibleQuantity ?? RemainingQuantity, RemainingQuantity);
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
            DisplayedQuantity -= quantity;

            if (RemainingQuantity == 0)
            {
                Status = OrderStatus.Filled;
                CompletedTime = time;
            }
        }

        // An auction print allocates full remaining size in one shot rather than peeling from the
        // displayed peak. DisplayedQuantity is re-derived from what's left rather than subtracted
        // from, since quantity here isn't capped to the peak and could take it negative. Bumps
        // neither SequenceNumber nor ModifiedTime: the order keeps its place in the queue.
        public void FillFullSize(DateTime time, int quantity)
        {
            FilledQuantity += quantity;
            RemainingQuantity -= quantity;
            DisplayedQuantity = Math.Min(MaxVisibleQuantity ?? RemainingQuantity, RemainingQuantity);

            if (RemainingQuantity == 0)
            {
                Status = OrderStatus.Filled;
                CompletedTime = time;
            }
        }

        // Peak exhausted with reserve remaining. Bumps SequenceNumber/ModifiedTime because the
        // caller requeues to the back of the level straight after, as CME and Eurex both do.
        public void Replenish(long sequenceNumber, DateTime time)
        {
            DisplayedQuantity = Math.Min(MaxVisibleQuantity!.Value, RemainingQuantity);
            SequenceNumber = sequenceNumber;
            ModifiedTime = time;
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
