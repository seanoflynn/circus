using System;

namespace Circus.Common
{	
	public enum SettlementPriceType : uint
	{
		None 		= 0,
		Final		= 1 << 0,
		Actual		= 1 << 1,
		Rounded		= 1 << 2,
		Intraday	= 1 << 3,
		Null		= 1 << 7
	}	
}