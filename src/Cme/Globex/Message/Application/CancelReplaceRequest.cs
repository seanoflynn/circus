using System;

using Circus.Common;
using Circus.Fix;

namespace Circus.Cme
{
	public class CancelReplaceRequest : CreateUpdateRequest
	{
		[BoolField(31, Tag.OFMOverride)]
		public bool? OfmOverride { get; set; }

		public CancelReplaceRequest() : base(MessageType.OrderCancelReplaceRequest) { }

		public CancelReplaceRequest(string account, string orderId, string clientId, string previousClientId, string correlationClientId,
							 Security security,
							 OrderType orderType, TimeInForce tif, DateTime? expiry,
							 Side side, int? price, int? stopPrice,
							 int quantity, int? minQuantity, int? maxShow)
			: base(MessageType.OrderCancelReplaceRequest, account, orderId, clientId, 
			       previousClientId, correlationClientId, security,
			       orderType, tif, expiry, side, price, stopPrice, 
			       quantity, minQuantity, maxShow)
		{ }
	}
}