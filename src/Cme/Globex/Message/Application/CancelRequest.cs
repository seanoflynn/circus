using System;

using Circus.Common;
using Circus.Fix;

namespace Circus.Cme
{
	public class CancelRequest : CreateUpdateDeleteRequest
	{
		public CancelRequest() : base(MessageType.OrderCancelRequest) { }

		public CancelRequest(string orderId, string clientId, string previousClientId, 
		                          string correlationClientId, Security security, Side side, string account) 
			: base(MessageType.OrderCancelRequest, orderId, clientId, previousClientId, correlationClientId, security, side, account)
		{ }
	}
}
