using System;

using Circus.Common;
using Circus.Fix;

namespace Circus.Cme
{
	public class NewOrder : CreateUpdateRequest
	{
		public NewOrder() : base(MessageType.OrderSingle) { }

		public NewOrder(string account, string clientId,
							 Security security,
							 OrderType orderType, TimeInForce tif, DateTime? expiry,
							 Side side, int? price, int? stopPrice,
							 int quantity, int? minQuantity, int? maxShow)
			: base(MessageType.OrderSingle, account, null, clientId, null, clientId, security,
			       orderType, tif, expiry, side, price, stopPrice, 
			       quantity, minQuantity, maxShow)
		{ }
	}
}
