using System;

namespace Circus.OrderBook
{
    internal class InternalOrder
    {
        public long SequenceNumber { get; private set; }
        public Guid Id { get; }
        public Security Security { get; }
        public DateTime CreatedTime { get; }
        public DateTime ModifiedTime { get; private set; }
        public DateTime? CompletedTime { get; private set; }
        public TimeInForce TimeInForce { get; }
        public Side Side { get; }
        public decimal Price { get; private set; }
        public int Quantity { get; private set; }
        public int RemainingQuantity { get; private set; }
        public int FilledQuantity { get; private set; }
        public OrderStatus Status { get; private set; } = OrderStatus.Working;

        public InternalOrder(long sequenceNumber, Guid id, Security security, DateTime time, TimeInForce timeInForce,
            Side side,
            decimal price, int quantity)
        {
            SequenceNumber = sequenceNumber;
            Id = id;
            Security = security;
            CreatedTime = time;
            ModifiedTime = time;
            TimeInForce = timeInForce;
            Side = side;
            Price = price;
            Quantity = quantity;
            RemainingQuantity = Quantity;
            FilledQuantity = 0;
        }

        public override string ToString() => 
            $"[Order #{Id} {Status} {ModifiedTime:HH:mm:ss} {Side} {Quantity}@{Price}]";

        public Order ToOrder()
        {
            return new(Id, Security, CreatedTime, ModifiedTime, CompletedTime, Status, OrderType.Limit, TimeInForce,
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
    }
}