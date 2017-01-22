using System;

namespace Circus.Common
{
	public enum OrderRejectReason
	{
		MarketClosed = 1003,
		MarketPaused = 1003,
		FixFieldMissingOrIncorrect = 1007,
		RequiredFieldMissing = 1010,
		FixFieldIncorrect = 1011,
		PriceMustBeGreaterThanZero = 1012,
		InvalidOrderQualifier = 1013,
		UnauthorizedUser = 1014,
		NoOrdersToMatchMarketOrder = 2013,
		InvalidExpireDate = 2019,
		OrderNotInBook = 2045,
		InvalidDisclosedQuantity = 2046,
		UnknownContract = 2047,
		DifferentSenderCompID = 2048,
		DifferentSide = 2051,
		DifferentGroup = 2052,
		DifferentSecurityType = 2053,
		DifferentAccount = 2054,
		DifferentQuantity = 2055,
		DifferentTraderID = 2056,
		InvalidStopPriceMustBeGreaterThanEqualTriggerPrice = 2058,
		InvalidStopPriceMustBeLessThanEqualTriggerPrice = 2059,
		StopPriceMustBeLessThanLastTradePrice = 2060,
		StopPriceMustBeGreaterThanLastTradePrice = 2061,
		DifferentProduct = 2100,
		DifferentIffMitigationStatus = 2101,
		DifferentSenderCompID2 = 2102,
		DifferentTraderID2 = 2103,
		QuantityOutOfRange = 2115,
		TypeMarketPreOpenPostClose = 2130,
		PriceOutsideLimits = 2137,
		PriceOutsideBands = 2179,
		TypeNotPermitted = 2311,
		InstrumentHasRequestForCrossInProgress = 2500,
		QuantityTooLow = 2501,
		OrderRejected = 7000,
		MarketNoCancel = 7024,
		MarketReserved = 7027,
		InvalidSessionDate = 7028,
		MarketForbidden = 7029,
		InvalidDisclosedQuantity2 = 7613,
	}	
}
