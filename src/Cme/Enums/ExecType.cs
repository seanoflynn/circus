using System;

namespace Circus.Common
{

	public enum ExecType
	{
		New = '0',
		PartialFill = '1',
		Fill = '2',
		DoneForDay = '3',
		Cancel = '4',
		Replace = '5',
		PendingCancelReplace = '6',
		Stopped = '7',
		Rejected = '8',
		Suspended = '9',
		PendingNew = 'A',
		Calculated = 'B',
		Expired = 'C',
	}
}
