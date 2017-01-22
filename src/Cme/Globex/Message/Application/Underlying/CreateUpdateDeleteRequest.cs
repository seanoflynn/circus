using System;

using Circus.Common;
using Circus.Fix;

namespace Circus.Cme
{	
	public abstract class CreateUpdateDeleteRequest : Message
	{	
		[StringField(1, Tag.OrderID)]
		public string OrderId { get; set; }

		[StringField(2, Tag.ClOrdID)]
		public string ClientOrderId { get; set; }

		[StringField(3, Tag.OrigClOrdID)]
		public string PreviousClientOrderId { get; set; }

		[StringField(4, Tag.CorrelationClOrdID)]
		public string CorrelationClientOrderId { get; set; }

		[EnumField(5, Tag.SecurityType, true)]
		public SecurityType SecurityType { get; set; }

		[StringField(6, Tag.Symbol)]
		public string Product { get; set; }

		[StringField(7, Tag.SecurityDesc)]
		public string Contract { get; set; }

		[EnumField(11, Tag.Side)]
		public Side Side { get; set; }

		[DateTimeField(17, Tag.TransactTime)]
		public DateTime TransactTime { get; set; }

		[StringField(20, Tag.Account)]
		public string Account { get; set; }

		[BoolField(23, Tag.ManualOrderIndicator)]
		public bool IsManualOrder { get; set; }

		public CreateUpdateDeleteRequest(MessageType type) : base(type) { }

		public CreateUpdateDeleteRequest(MessageType type, string orderId, string clientId, 
		                                 string prevClientId, string correlationClientId, Security security,
		                                 Side side, string account) : this(type)
		{
			OrderId = orderId;
			ClientOrderId = clientId;
			PreviousClientOrderId = prevClientId;
			CorrelationClientOrderId = correlationClientId;

			SecurityType = security.Type;
			Product = security.Product;
			Contract = security.Contract;

			Side = side;

			TransactTime = DateTime.UtcNow;

			Account = account;
			IsManualOrder = false;
		}
	}
}
