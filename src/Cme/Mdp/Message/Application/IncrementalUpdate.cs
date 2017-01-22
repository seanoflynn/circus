using System;
using System.Collections.Generic;

using Circus.Common;
using Circus.Fix;

namespace Circus.Cme
{	
	public class IncrementalUpdate : MdpMessage
	{				
		[BitfieldField(1, Tag.MatchEventIndicator, 8)]
		public MatchEventIndicator MatchEventIndicator { get; set; }

		[DateTimeField(2, Tag.TransactTime)]
		public DateTime Time { get; set; }

		[GroupField(3, Tag.NoMDEntries)]
		public List<MarketDataUpdateDataBlock> MDEntries { get; set; } = new List<MarketDataUpdateDataBlock>();

		[GroupField(4, Tag.NoOrderIDEntries)]
		public List<IncrementalUpdateOrderEntry> NoOrderIDEntries { get; set; } = new List<IncrementalUpdateOrderEntry>();

		public IncrementalUpdate() : base(MessageType.MarketDataIncrementalRefresh)
		{ }

		public IncrementalUpdate(MatchEventIndicator mei, DateTime time) 
			: base(MessageType.MarketDataIncrementalRefresh)
		{			
			MatchEventIndicator = mei;
			Time = time;
		}
	}
}
