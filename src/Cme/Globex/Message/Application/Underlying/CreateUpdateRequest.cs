using System;

using Circus.Common;
using Circus.Fix;

namespace Circus.Cme
{
	public abstract class CreateUpdateRequest : CreateUpdateDeleteRequest
	{
		[EnumField(8, Tag.OrdType, true)]
		public OrderType OrderType { get; set; }

		[EnumField(9, Tag.TimeInForce)]
		public TimeInForce TimeInForce { get; set; }

		[DateTimeField(10, Tag.ExpireDate, DateTimeFormat.LocalMktDate)]
		public DateTime? ExpireDate { get; set; }

		[IntField(12, Tag.Price)]
		public int? Price { get; set; }

		[IntField(13, Tag.StopPx)]
		public int? StopPrice { get; set; }

		[IntField(14, Tag.OrderQty, 0, int.MaxValue)]
		public int Quantity { get; set; }

		[IntField(15, Tag.MinQty, 0, int.MaxValue)]
		public int? MinQuantity { get; set; }

		[IntField(16, Tag.MaxShow, 0, int.MaxValue)]
		public int? MaxVisibleQuantity { get; set; }

		[StringField(18, Tag.SelfMatchPreventionID)]
		public string SelfMatchPreventionId { get; set; }

		[EnumField(19, Tag.SelfMatchPreventionInstruction)]
		public SelfMatchPreventionInstruction? SelfMatchPreventionInstruction { get; set; }

		[StringField(21, Tag.NoAllocs)]
		public string NoAllocs { get; set; }

		[StringField(22, Tag.AllocAccount)]
		public string AllocAccount { get; set; }

		[EnumField(24, Tag.HandlInst)]
		public HandlingInstruction HandlingInstruction { get; set; }

		[EnumField(25, Tag.CustOrderHandlingInst, true)]
		public CustomerOrderHandlingInstruction? CustomerOrderHandlingInstruction { get; set; }

		[BoolField(26, Tag.PreTradeAnonymity)]
		public bool? PreTradeAnonymity { get; set; }

		[EnumField(27, Tag.CustomerOrFirm)]
		public CustomerOrFirm CustomerOrFirm { get; set; }

		[EnumField(28, Tag.CtiCode)]
		public CtiCode CtiCode { get; set; } = CtiCode.Cti4;

		[StringField(29, Tag.GiveUpFirm)]
		public string GiveUpFirm { get; set; }

		[EnumField(30, Tag.CmtaGiveupCD, true)]
		public CmtaGiveupCD? CmtaGiveupCD { get; set; }

		public CreateUpdateRequest(MessageType type) : base(type) { }

		public CreateUpdateRequest(MessageType type, string account, string orderId,
								   string clientId, string prevClientId, string correlationClientId,
									 Security security,
									 OrderType orderType, TimeInForce tif, DateTime? expiry,
									 Side side, int? price, int? stopPrice,
									 int quantity, int? minQuantity, int? maxShow)
			: base(type, orderId, clientId, prevClientId, correlationClientId, security, side, account)
		{
			OrderType = orderType;
			TimeInForce = tif;
			ExpireDate = expiry;

			Price = price;
			StopPrice = stopPrice;

			Quantity = quantity;
			MinQuantity = minQuantity;
			MaxVisibleQuantity = maxShow;

			HandlingInstruction = HandlingInstruction.AutomatedExecution;
			CustomerOrFirm = CustomerOrFirm.Customer;
		}
	}
}