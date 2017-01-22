using System;

using Circus.Common;
using Circus.Fix;

namespace Circus.Cme
{
	public class CancelReject : Message
	{
		[StringField(1, Tag.OrderID)]
		public string OrderId { get; set; }

		[StringField(2, Tag.ClOrdID)]
		public string ClientOrderId { get; set; }

		[StringField(3, Tag.OrigClOrdID)]
		public string PreviousClientOrderId { get; set; }

		[StringField(4, Tag.CorrelationClOrdID)]
		public string CorrelationClientOrderId { get; set; }

		[StringField(5, Tag.SecurityID)]
		public string SecurityId { get; set; }

		[StringField(6, Tag.SecurityDesc)]
		public string Contract { get; set; }

		[DateTimeField(7, Tag.TransactTime)]
		public DateTime TransactTime { get; set; }

		[DateTimeField(8, Tag.RequestTime)]
		public DateTime? RequestTime { get; set; }

		[StringField(9, Tag.Account)]
		public string Account { get; set; }

		[StringField(10, Tag.SelfMatchPreventionID)]
		public string SelfMatchPreventionId { get; set; }

		[BoolField(11, Tag.ManualOrderIndicator)]
		public bool IsManualOrder { get; set; }

		[BoolField(12, Tag.PreTradeAnonymity)]
		public bool? PreTradeAnonymity { get; set; } = true;

		[StringField(13, Tag.ExecID)]
		public string ExecutionId { get; set; }

		[EnumField(14, Tag.OrdStatus, true)]
		public OrderStatus OrderStatus { get; set; }

		[EnumField(15, Tag.CxlRejReason)]
		public CancelRejectReason CancelRejectReason { get; set; }

		[EnumField(16, Tag.CxlRejResponseTo)]
		public CancelRejectResponseTo CancelRejectResponseTo { get; set; }

		[StringField(17, Tag.Text)]
		public string Text { get; set; }

		public CancelReject() : base(MessageType.OrderCancelReject)
		{ }

		public CancelReject(Order order, OrderInfo orderInfo, CancelRejectResponseTo responseTo, CancelRejectReason reason)
			: base(MessageType.OrderCancelReject)
		{
			OrderId = orderInfo.OrderId;
			ClientOrderId = orderInfo.ClientId;
			PreviousClientOrderId = orderInfo.PreviousClientId;
			CorrelationClientOrderId = orderInfo.CorrelationClientId;

			SecurityId = order.Security.Id.ToString();
			Contract = order.Security.Contract;

			TransactTime = DateTime.UtcNow;

			SelfMatchPreventionId = order.SelfMatchId;

			Account = orderInfo.Account;
			IsManualOrder = orderInfo.IsManual;
			PreTradeAnonymity = orderInfo.PreTradeAnonymity;

			ExecutionId = Guid.NewGuid().ToString();
			OrderStatus = OrderStatus.Undefined;

			CancelRejectResponseTo = responseTo;
			CancelRejectReason = reason;
			Text = "lookup table";
		}
	}
}
