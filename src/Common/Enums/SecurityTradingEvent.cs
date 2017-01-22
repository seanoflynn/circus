using System;

namespace Circus.Common
{

	public enum SecurityTradingEvent
	{
		NoEvent = 0,
		NoCancel = 1,
		ChangeOfTradingSession = 4,
		ImpliedMatchingOn = 5,
		ImpliedMatchingOff = 6,
	}
	
}
