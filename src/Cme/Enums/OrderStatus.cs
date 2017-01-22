using System;

namespace Circus.Cme
{
	public enum OrderStatus
	{
		New = '0',
		PartiallyFilled = '1',
		Filled = '2',
		DoneForDay = '3',
		Cancelled = '4',
		Replaced = '5',
		PendingCancelReplace = '6',
		Stopped = '7',
		Rejected = '8',
		Suspended = '9',
		PendingNew = 'A',
		Calculated = 'B',
		Expired = 'C',
		Undefined = 'U',
	}
}
