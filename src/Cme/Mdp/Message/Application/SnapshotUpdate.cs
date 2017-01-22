using System;
using System.Collections.Generic;

using Circus.Common;
using Circus.Fix;

namespace Circus.Cme
{
	public class SnapshotUpdate : MdpMessage
	{
		[IntField(1, Tag.LastMsgSeqNumProcessed, 0, int.MaxValue)]
		public int LastMsgSeqNumProcessed { get; set; }

		[IntField(2, Tag.TotNumReports)]
		public int TotNumReports { get; set; }

		[IntField(3, Tag.SecurityID)]
		public int SecurityId { get; set; }

		[IntField(4, Tag.RptSeq)]
		public int RptSeq { get; set; }

		[DateTimeField(5, Tag.TransactTime)]
		public DateTime TransactTime { get; set; }

		[EnumField(6, Tag.MDSecurityTradingStatus)]
		public SecurityTradingStatus TradingStatus { get; set; }

		[DateTimeField(7, Tag.TradeDate, DateTimeFormat.LocalMktDate)]
		public DateTime TradeDate { get; set; }

		[DateTimeField(8, Tag.LastUpdateTime)]
		public DateTime LastUpdateTime { get; set; }

		[GroupField(9, Tag.NoMDEntries)]
		public List<MarketDataUpdateDataBlock> MDEntries { get; set; } = new List<MarketDataUpdateDataBlock>();

		public SnapshotUpdate() : base(MessageType.MarketDataSnapshotFullRefresh)
		{ }

		public SnapshotUpdate(Security security, SecurityTradingStatus status) 
			: base(MessageType.MarketDataSnapshotFullRefresh)
		{
			LastMsgSeqNumProcessed = 0;
			TotNumReports = 1;

			SecurityId = security.Id;
			RptSeq = 0;

			TransactTime = DateTime.UtcNow;
			TradeDate = DateTime.Today;

			TradingStatus = status;
		}
	}
}
