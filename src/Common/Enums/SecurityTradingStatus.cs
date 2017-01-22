using System;

namespace Circus.Common
{
	public enum SecurityTradingStatus
	{
		Halt = 2,
		Close = 4,
		NewPriceIndication = 15, // NoCancel
		Open = 17,
		NotAvailable = 18,
		UnknownInvalid = 20,
		PreOpen = 21,
		PreCross = 24,
		Cross = 25,
		PostClose = 26,
		NoChange = 103,
	}	
}