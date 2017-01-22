using System;

using Circus.Common;
using Circus.Fix;

namespace Circus.Cme
{
	public class CancelAck : AckExecutionReport
	{
		public CancelAck() : base(ExecType.Cancel, OrderStatus.Cancelled) { }

		public CancelAck(Order order, OrderInfo orderInfo, ExecRestatementReason? reason = null)
			: base(ExecType.Cancel, OrderStatus.Cancelled, order, orderInfo, reason)
		{ }
	}
}