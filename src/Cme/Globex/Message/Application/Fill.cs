using System;

using Circus.Common;
using Circus.Fix;

namespace Circus.Cme
{
	public class Fill : ExecutionReport
	{
		[StringField(32, Tag.SecondaryExecID)]
		public string SecondaryExecId { get; set; }

		[DateTimeField(36, Tag.TradeDate, DateTimeFormat.LocalMktDate)]
		public DateTime TradeDate { get; set; }

		[IntField(37, Tag.LastPx)]
		public int FillPrice { get; set; }

		[IntField(38, Tag.LastQty, 0, int.MaxValue)]
		public int FillQuantity { get; set; }

		[BoolField(39, Tag.AggressorIndicator)]
		public bool IsAggressor { get; set; }

		[StringField(40, Tag.TotalNumSecurities)]
		public string TotalNumSecurities { get; set; }

		[EnumField(41, Tag.MultiLegReportingType)]
		public MultiLegReportingType MultiLegReportingType { get; set; }

		[StringField(42, Tag.CrossType)]
		public string CrossType { get; set; }

		[StringField(43, Tag.CrossID)]
		public string CrossID { get; set; }

		[StringField(44, Tag.ContraTrader)]
		public string ContraTrader { get; set; } = "TRADE";

		[StringField(45, Tag.ContraBroker)]
		public string ContraBroker { get; set; } = "CME000A";

		public Fill(bool isPartial) 
			: base(isPartial ? ExecType.PartialFill : ExecType.Fill,
				   isPartial ? OrderStatus.PartiallyFilled : OrderStatus.Filled) 
		{ }

		public Fill(Order order, OrderInfo orderInfo, DateTime time, int price, int quantity, bool isAggressor, bool isPartial)
			: base(isPartial ? ExecType.PartialFill : ExecType.Fill,
				   isPartial ? OrderStatus.PartiallyFilled : OrderStatus.Filled,
			       order, orderInfo)
		{
			TransactTime = time;
			FillPrice = price;
			FillQuantity = quantity;
			IsAggressor = isAggressor;
			TradeDate = time;
		}
	}
}