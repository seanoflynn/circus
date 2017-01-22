using System;

namespace Circus.Common
{

	public enum MDEntryType
	{
		Bid = '0',
		Offer = '1',
		ImpliedBid = 'E',
		ImpliedOffer = 'F',
		TradeSummary = '2',
		OpeningPrice = '4',
		SettlementPrice = '6',
		TradingSessionHighPrice = '7',
		TradingSessionLowPrice = '8',
		SessionHighBid = 'N',
		SessionLowOffer = 'O',
		TradeVolume = 'B',
		OpenInterest = 'C',
		FixingPrice = 'W',
		EmptyBook = 'J',
		ElectronicVolume = 'e',
		Limits = 'g',
	}
	
}
