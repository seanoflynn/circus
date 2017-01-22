using System;
using System.Collections.Generic;

namespace Circus.Common
{
	public enum SecurityIdSource
	{
		Cusip = '1',
		Sedol = '2',
		Quik = '3',
		Isin = '4',
		RicCode = '5',
		IsoCurrencyCode = '6',
		IsoCountryCode = '7',
		ExchangeSymbol = '8',
		CtaSymbol = '9', // Consolidated Tape Association
		BloombergSymbol = 'A',
		Wertpapier = 'B',
		Dutch = 'C',
		Valoren = 'D',
		Sicovam = 'E',
		Belgian = 'F',
		Common = 'G', // (Clearstream and Euroclear)
		ClearingHouse = 'H',
		IsdaFpmlProductSpecification = 'I',
		OptionsPriceReportingAuthority = 'J',
	}
	
}