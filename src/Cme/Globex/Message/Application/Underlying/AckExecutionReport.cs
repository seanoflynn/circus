using System;

using Circus.Common;
using Circus.Fix;

namespace Circus.Cme
{
	public abstract class AckExecutionReport : ExecutionReport
	{
		[EnumField(30, Tag.ExecRestatementReason, false)]
		public ExecRestatementReason? ExecRestatementReason { get; set; }

		public AckExecutionReport(ExecType execType, OrderStatus orderStatus) 
			: base(execType, orderStatus) 
		{ }

		public AckExecutionReport(ExecType execType, OrderStatus orderStatus, Order order, OrderInfo orderInfo, 
		                          ExecRestatementReason? reason = null)
			: base(execType, orderStatus, order, orderInfo)
		{ 
			ExecRestatementReason = reason;
		}
	}
}