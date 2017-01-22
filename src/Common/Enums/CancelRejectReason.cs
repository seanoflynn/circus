using System;

namespace Circus.Common
{
	public enum CancelRejectReason
	{
		TooLateToCancel = 0,
		UnknownOrder = 1,
		BrokerOption = 2,
		PendingCancelOrReplace = 3,
		UnableToProcessOrderMassCancel = 4,
		OrigOrdModTimeTransactTime = 5,
		DuplicateClOrdID = 6,
		PriceExceedsCurrentPrice = 7,
		PriceExceedsCurrentPriceBand = 8,
		InvalidPriceIncrement = 18,
		Other = 99,
		OrderIsNotInBook = 2045, 
		MarketIsClosed = 1003,
	}
	
}
