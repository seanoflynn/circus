using System;
using Circus.Enums;

namespace Circus.OrderBook
{
    internal class InternalOrder
    {
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

        public InternalOrder(Guid id, Security security, DateTime time, TimeInForce timeInForce, Side side,
            decimal price, int quantity)
        {
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

        public override string ToString()
        {
            return $"[Order #{Id} {ModifiedTime:HH:mm:ss} {Side,-4} {Quantity}@{Price}]";
        }

        public Order ToOrder()
        {
            return new(Id, Security, CreatedTime, ModifiedTime, CompletedTime, Status, OrderType.Limit, TimeInForce,
                Side, Price, null, Quantity, FilledQuantity, RemainingQuantity);
        }

        public void Delete(DateTime time)
        {
            RemainingQuantity = 0;
            CompletedTime = time;
            Status = OrderStatus.Deleted;
        }

        public void Update(DateTime time, decimal price, int quantity)
        {
            // TODO: validate quantity

            if (price != Price || quantity > Quantity)
            {
                ModifiedTime = time;
            }

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