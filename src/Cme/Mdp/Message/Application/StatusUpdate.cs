using System;

using Circus.Common;
using Circus.Fix;

namespace Circus.Cme
{	
	public class StatusUpdate : MdpMessage
	{
        [BitfieldField(1, Tag.MatchEventIndicator, 8)]
        public MatchEventIndicator MatchEventIndicator { get; set; }

		[DateTimeField(2, Tag.TransactTime)]
		public DateTime TransactTime { get; set; }

		[DateTimeField(3, Tag.TradeDate, DateTimeFormat.LocalMktDate)]
		public DateTime TradeDate { get; set; }

		[StringField(4, Tag.SecurityGroup)]
		public string Product { get; set; }

		[StringField(5, Tag.Asset)]
		public string Group { get; set; }

		[StringField(6, Tag.SecurityID)]
		public int? Id { get; set; }

		[EnumField(7, Tag.SecurityTradingStatus)]
		public SecurityTradingStatus Status { get; set; }

		[EnumField(8, Tag.HaltReason)]
		public StatusChangeReason StatusChangeReason { get; set; }

		[EnumField(9, Tag.SecurityTradingEvent)]
		public SecurityTradingEvent Event { get; set; }

		public StatusUpdate() : base(MessageType.SecurityStatus)
		{}

		public StatusUpdate(StatusUpdateType type, Security security, SecurityTradingStatus status, StatusChangeReason reason, SecurityTradingEvent ev) 
			: base(MessageType.SecurityStatus)
		{
			MatchEventIndicator = MatchEventIndicator.LastMessage;

			TransactTime = DateTime.UtcNow;
			TradeDate = DateTime.Today;

			if (type == StatusUpdateType.Security)
				Id = security.Id;
			else if (type == StatusUpdateType.Product)
				Product = security.Product;
			else
				Group = security.Group;

			Status = status;
			StatusChangeReason = reason;
			Event = ev;
		}
	}
}
