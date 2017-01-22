using System;

namespace Circus.Common
{	
	public enum InstrumentAttributeValue : uint
	{
		None = 0,
		ElectronicMatchEligible = 0,
		OrderCrossEligible = 1,
		BlockTradeEligible = 2,
		EfpEligible = 3,
		EbfEligible = 4,
		EfsEligible = 5,
		EfrEligible = 6,
		OtcEligible = 7,
		MassQuotingEligible = 8,
		NegativeStrikeEligible = 9,
		NegativePriceEligible = 10,
		IsFractional = 11,
		RfqCrossEligible = 13,
		ZeroPriceEligible = 14,
		DecayingProductEligibility = 15,
		VariableQuantityProductEligibility = 16,
		DailyProductEligibility = 17,
		GtOrdersEligibility = 18,
		ImpliedMatchingEligibility = 19,
	}	
}