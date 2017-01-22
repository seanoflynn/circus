using System;

using Circus.Common;
using Circus.Fix;

namespace Circus.Cme
{
	public abstract class ExecutionReport : Message
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

		[EnumField(6, Tag.SecurityType, true)]
		public SecurityType SecurityType { get; set; }

		[StringField(7, Tag.Symbol)]
		public string Product { get; set; }

		[StringField(8, Tag.SecurityDesc)]
		public string Contract { get; set; }

		[EnumField(9, Tag.OrdType, true)]
		public OrderType OrderType { get; set; }

		[EnumField(10, Tag.TimeInForce)]
		public TimeInForce TimeInForce { get; set; }

		[DateTimeField(11, Tag.ExpireDate, DateTimeFormat.LocalMktDate)]
		public DateTime? ExpireDate { get; set; }

		[EnumField(12, Tag.Side)]
		public Side Side { get; set; }

		[IntField(13, Tag.Price)]
		public int? Price { get; set; }

		[IntField(14, Tag.StopPx)]
		public int? StopPrice { get; set; }

		[IntField(15, Tag.OrderQty, 0, int.MaxValue)]
		public int Quantity { get; set; }

		[IntField(16, Tag.MinQty, 0, int.MaxValue)]
		public int? MinQuantity { get; set; }

		[IntField(17, Tag.MaxShow, 0, int.MaxValue)]
		public int? MaxVisibleQuantity { get; set; }

		[DateTimeField(18, Tag.TransactTime)]
		public DateTime TransactTime { get; set; }

		[DateTimeField(19, Tag.RequestTime)]
		public DateTime? RequestTime { get; set; }

		[StringField(20, Tag.SelfMatchPreventionID)]
		public string SelfMatchPreventionId { get; set; }

		[EnumField(21, Tag.SelfMatchPreventionInstruction)]
		public SelfMatchPreventionInstruction? SelfMatchPreventionInstruction { get; set; }

		[StringField(22, Tag.Account)]
		public string Account { get; set; }

		[StringField(23, Tag.NoAllocs)]
		public string NoAllocs { get; set; }

		[StringField(24, Tag.AllocAccount)]
		public string AllocAccount { get; set; }

		[BoolField(25, Tag.ManualOrderIndicator)]
		public bool IsManualOrder { get; set; }

		[EnumField(26, Tag.CustOrderHandlingInst, true)]
		public CustomerOrderHandlingInstruction? CustomerOrderHandlingInstruction { get; set; }

		[BoolField(27, Tag.PreTradeAnonymity)]
		public bool? PreTradeAnonymity { get; set; }

		[EnumField(28, Tag.ExecTransType)]
		public ExecTransType ExecTransType { get; set; }

		[EnumField(29, Tag.ExecType, true)]
		public ExecType ExecType { get; set; }

		[StringField(31, Tag.ExecID)]
		public string ExecutionId { get; set; }

		[EnumField(32, Tag.OrdStatus, true)]
		public OrderStatus OrderStatus { get; set; }

		[IntField(33, Tag.AvgPx)]
		public int AverageFilledPrice { get; set; }

		[IntField(34, Tag.CumQty, 0, int.MaxValue)]
		public int FilledQuantity { get; set; }

		[IntField(35, Tag.LeavesQty, 0, int.MaxValue)]
		public int RemainingQuantity { get; set; }

		public ExecutionReport(ExecType execType, OrderStatus orderStatus)
			: base(MessageType.ExecutionReport)
		{
			ExecTransType = ExecTransType.New;
			ExecType = execType;
			OrderStatus = orderStatus;
		}

		public ExecutionReport(ExecType execType, OrderStatus orderStatus, Order order, OrderInfo orderInfo) 
			: base(MessageType.ExecutionReport) 
		{
			OrderId = orderInfo.OrderId;
			ClientOrderId = orderInfo.ClientId;
			PreviousClientOrderId = orderInfo.PreviousClientId;
			CorrelationClientOrderId = orderInfo.CorrelationClientId;

			SecurityId = order.Security.Id.ToString();
			SecurityType = order.Security.Type;
			Product = order.Security.Product;
			Contract = order.Security.Contract;

			OrderType = order.Type;
			TimeInForce = order.TimeInForce;
			ExpireDate = order.ExpireDate;

			Side = order.Side;

			Price = order.Price;
			StopPrice = order.StopPrice;

			Quantity = order.Quantity;
			MinQuantity = order.MinQuantity;
			MaxVisibleQuantity = order.MaxVisibleQuantity;

			TransactTime = DateTime.UtcNow;

			SelfMatchPreventionId = order.SelfMatchId;
			SelfMatchPreventionInstruction = order.SelfMatchMode;

			Account = orderInfo.Account;

			IsManualOrder = orderInfo.IsManual;
			CustomerOrderHandlingInstruction = Cme.CustomerOrderHandlingInstruction.AlgoEngine;
			PreTradeAnonymity = false;

			ExecTransType = ExecTransType.New;
			ExecType = execType;
			ExecutionId = Guid.NewGuid().ToString();

			OrderStatus = orderStatus;
			AverageFilledPrice = 0;
			FilledQuantity = order.FilledQuantity;
			RemainingQuantity = order.RemainingQuantity;
		}
	}
}
