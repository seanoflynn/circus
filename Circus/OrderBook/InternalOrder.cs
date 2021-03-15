using System;

namespace Circus.OrderBook
{
    internal class InternalOrder
    {
        public long SequenceNumber { get; private set; }
        public Guid ClientId { get; }
        public Guid OrderId { get; }
        public Security Security { get; }
        public DateTime CreatedTime { get; }
        public DateTime ModifiedTime { get; private set; }
        public DateTime? CompletedTime { get; private set; }
        public OrderType Type { get; private set; }
        public OrderValidity Validity { get; }
        public Side Side { get; }
        public decimal Price { get; private set; }
        public int Quantity { get; private set; }
        public int RemainingQuantity { get; private set; }
        public int FilledQuantity { get; private set; }
        public OrderStatus Status { get; private set; } = OrderStatus.Working;

        public InternalOrder(long sequenceNumber, Guid clientId, Guid orderId, Security security, DateTime time,
            OrderType type, OrderValidity validity, Side side, decimal price, int quantity)
        {
            SequenceNumber = sequenceNumber;
            ClientId = clientId;
            OrderId = orderId;
            Security = security;
            CreatedTime = time;
            Type = type;
            ModifiedTime = time;
            Validity = validity;
            Side = side;
            Price = price;
            Quantity = quantity;
            RemainingQuantity = Quantity;
            FilledQuantity = 0;
        }

        public override string ToString() => 
            $"[Order #{OrderId} {Status} {ModifiedTime:HH:mm:ss} {Side} {Quantity}@{Price}]";

        public Order ToOrder()
        {
            return new(ClientId, OrderId, Security, CreatedTime, ModifiedTime, CompletedTime, Status, Type, Validity,
                Side, Price, null, Quantity, FilledQuantity, RemainingQuantity);
        }

        public void Cancel(DateTime time)
        {
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

        public void Update(long sequenceNumber, DateTime time, decimal price, int quantity)
        {
            // TODO: validate quantity

            if (price != Price || quantity > Quantity)
            {
                SequenceNumber = sequenceNumber;
            }

            ModifiedTime = time;
            Price = price;
            RemainingQuantity -= (Quantity - quantity);
            Quantity = quantity;
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

        public void ConvertToLimit()
        {
            Type = OrderType.Limit;
        }
    }
}