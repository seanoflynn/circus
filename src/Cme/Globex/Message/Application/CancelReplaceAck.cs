using System;

using Circus.Common;
using Circus.Fix;

namespace Circus.Cme
{
	public class CancelReplaceAck : AckExecutionReport
	{
		public CancelReplaceAck() : base(ExecType.Replace, OrderStatus.Replaced)
		{ }

		public CancelReplaceAck(Order order, OrderInfo orderInfo)
			: base(ExecType.Replace, OrderStatus.Replaced, order, orderInfo)
		{ }
	}
}