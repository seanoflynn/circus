using System;

using Circus.Common;
using Circus.Fix;

namespace Circus.Cme
{
	public class NewOrderAck : AckExecutionReport
	{
		public NewOrderAck() : base(ExecType.New, OrderStatus.New) { }

		public NewOrderAck(Order order, OrderInfo orderInfo) : base(ExecType.New, OrderStatus.New, order, orderInfo)
		{ }
	}
}