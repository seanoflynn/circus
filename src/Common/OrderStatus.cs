using System;

namespace Circus.Common
{
	[Flags]
	public enum OrderStatus
	{		
		Undefined 		= 0,

		Created	  		= 1 << 0,
		Updated  		= 1 << 1,
		PartiallyFilled = 1 << 2,

		Deleted 		= 1 << 4,
		Filled 			= 1 << 3,
		Expired   		= 1 << 5,

		Hidden 			= 1 << 6,

		Working 		= Created | Updated | PartiallyFilled,
		Completed 		= Deleted | Filled | Expired,
	}
}