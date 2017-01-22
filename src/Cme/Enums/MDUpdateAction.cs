using System;

namespace Circus.Common
{
	public enum MDUpdateAction
	{
		New = 0,
		Change = 1,
		Delete = 2,
		DeleteThru = 3, // causes the book to be deleted from the top down to the specified Price and PriceLevel
		DeleteFrom = 4, // causes the book to be deleted from the specified Price and PriceLevel
		Overlay = 5,
	}
	
}
