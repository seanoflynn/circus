using System;

using Circus.Common;
using Circus.Fix;

namespace Circus.Cme
{
	public class Expire : ExecutionReport
	{
		public Expire() : base(ExecType.Expired, OrderStatus.Expired) 
		{ }

		public Expire(Order order, OrderInfo orderInfo) 
			: base(ExecType.Expired, OrderStatus.Expired, order, orderInfo)
		{ }
	}
}