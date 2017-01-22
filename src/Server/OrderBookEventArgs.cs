using System;
using System.Collections.Generic;

using Circus.Common;

namespace Circus.Server
{
	public class OrderBookEventArgs : EventArgs
	{
		public Order Order { get; private set; }

		public OrderBookEventArgs(Order order)
		{
			Order = order;
		}
	}

	public class OrderCreatedEventArgs : OrderBookEventArgs
	{
		public OrderCreatedEventArgs(Order order) : base(order) { }
	}

	public class OrderUpdateEventArgs : OrderBookEventArgs
	{
		public OrderUpdateEventArgs(Order order) : base(order) { }
	}

	public class OrderDeletedEventArgs : OrderBookEventArgs
	{
		public ExecRestatementReason? Reason { get; private set; }

		public OrderDeletedEventArgs(Order order, ExecRestatementReason? reason = null)
			: base(order)
		{
			Reason = reason;
		}
	}

	public class OrderCreateRejectedEventArgs : OrderBookEventArgs
	{
		public OrderRejectReason Reason { get; private set; }

		public OrderCreateRejectedEventArgs(Order order, OrderRejectReason reason)
			: base(order)
		{
			Reason = reason;
		}
	}

	public class OrderUpdateRejectedEventArgs : OrderBookEventArgs
	{
		public CancelRejectReason Reason { get; private set; }

		public OrderUpdateRejectedEventArgs(Order order, CancelRejectReason reason)
			: base(order)
		{
			Reason = reason;
		}
	}

	public class OrderDeleteRejectedEventArgs : OrderBookEventArgs
	{
		public CancelRejectReason Reason { get; private set; }

		public OrderDeleteRejectedEventArgs(Order order, CancelRejectReason reason)
			: base(order)
		{
			Reason = reason;
		}
	}

	public class OrderExpiredEventArgs : OrderBookEventArgs
	{
		public OrderExpiredEventArgs(Order order) : base(order) { }
	}

	public class OrderFilledEventArgs : OrderBookEventArgs
	{
		public DateTime Time { get; private set; }
		public int Price { get; private set; }
		public int Quantity { get; private set; }
		public bool IsAggressor { get; private set; }

		public OrderFilledEventArgs(Order order, DateTime time, int price, int quantity, bool isAggressor)
			: base(order)
		{
			Time = time;
			Price = price;
			Quantity = quantity;
			IsAggressor = isAggressor;
		}
	}

	public class Fill
	{
		public int OrderId { get; private set; }

		public Side Side { get; private set; }
		public int Price { get; private set; }
		public int Quantity { get; private set; }
		public bool IsAggressor { get; private set; }

		public Fill(int orderId, Side side, int price, int quantity, bool isAggressor)
		{
			OrderId = orderId;
			Side = side;

			Price = price;
			Quantity = quantity;
			IsAggressor = isAggressor;
		}
	}

	public class TradedEventArgs
	{
		public DateTime Time { get; private set; }
		public Security Security { get; set; }
		public List<Fill> Fills { get; set; }

		public TradedEventArgs(DateTime time, Security security, List<Fill> fills)
		{
			Time = time;
			Security = security;
			Fills = fills;
		}
	}

	public class BookUpdatedEventArgs
	{
		public DateTime Time { get; private set; }
		public Security Security { get; set; }

		public BookUpdatedEventArgs(DateTime time, Security security)
		{
			Time = time;
			Security = security;
		}
	}
}