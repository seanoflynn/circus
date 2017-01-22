using System;

namespace Circus.Common
{
	public enum ExecRestatementReason
	{
		Exchange = 8,
		Disconnect = 100,
		SelfMatchPreventionResting = 103,
		CreditControls = 104,
		FirmSoft = 105,
		RiskManagementApi = 106,
		SelfMatchPreventionAggressing = 107,
	}
}