using System;

using Circus.Common;
using Circus.Fix;

namespace Circus.Cme
{
	public class Reject : ExecutionReport
	{
		[EnumField(36, Tag.OrdRejReason)]
		public OrderRejectReason OrderRejectReason { get; set; }

		[StringField(37, Tag.Text)]
		public string Text { get; set; }

		public Reject() : base(ExecType.Rejected, OrderStatus.Rejected) { }

		public Reject(Order order, OrderInfo orderInfo, OrderRejectReason reason)
			: base(ExecType.Rejected, OrderStatus.Rejected, order, orderInfo)
		{
			OrderRejectReason = reason;
			Text = "lookup table";
		}
	}
}
