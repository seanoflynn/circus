using System;

namespace Circus.Common
{
	[Flags]
	public enum MatchEventIndicator : uint
	{
		None 			 = 0,
		LastTradeSummary = 1 << 0,
		LastVolume 		 = 1 << 1,
		LastRealQuote 	 = 1 << 2,
		LastStatistic 	 = 1 << 3,
		LastImpliedQuote = 1 << 4,
		Recovery 		 = 1 << 5,
		Reserved 		 = 1 << 6,
		LastMessage 	 = 1 << 7,
	}
	
}
