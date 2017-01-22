using System;

namespace Circus.Common
{
	public class Order
    {
		public int Id { get; set; }

		// TODO: change to LastModificationTime, add creation, modified, deleted, expired times
		public DateTime Time { get; set; }

		public OrderStatus Status { get; set; }
		public Security Security { get; set; }

		public OrderType Type { get; set; }
		public TimeInForce TimeInForce { get; set; }
		public DateTime? ExpireDate { get; set; }

		public Side Side { get; set; }

        public int? Price { get; set; }        
		public int? StopPrice { get; set; }

		public int Quantity { get; set; }
		public int FilledQuantity { get; set; }
		public int RemainingQuantity { get; set; }

		public int MinQuantity { get; set; }
		public int MaxVisibleQuantity { get; set; }

		public SelfMatchPreventionInstruction SelfMatchMode { get; set; }
		public string SelfMatchId { get; set; }

		public Order(int id, Security security, OrderType type, TimeInForce tif, DateTime? expiry, Side side,
					 int? price, int? stopPrice, int quantity, int minQuantity, int? maxVisibleQuantity,
		             string selfMatchId, SelfMatchPreventionInstruction selfMatchMode)
		{
			Id = id;
			Security = security;
			Type = type;
			TimeInForce = tif;
			ExpireDate = expiry;
			Side = side;
			Price = price;
			StopPrice = stopPrice;
			Quantity = quantity;
			FilledQuantity = 0;
			RemainingQuantity = quantity;
			MinQuantity = minQuantity;
			MaxVisibleQuantity = maxVisibleQuantity ?? int.MaxValue;
			SelfMatchId = selfMatchId;
			SelfMatchMode = selfMatchMode;
		}

		public override string ToString()
        {
			return $"[Order #{Id:D8} {Time:HH:mm:ss} {Side,-4} {RemainingQuantity}/{Quantity}@{Price}]";
        }
    }
}