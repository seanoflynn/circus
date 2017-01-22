using System;

namespace Circus.Common
{

	public enum StatusChangeReason
	{
		GroupSchedule = 0,
		SurveillanceIntervention = 1,
		MarketEvent = 2,
		InstrumentActivation = 3,
		InstrumentExpiration = 4,
		Unknown = 5,
		RecoveryInProcess = 6,
	}
	
}
