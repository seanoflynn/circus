using System;
using System.Collections.Generic;

namespace Circus.Common
{
	public enum SecurityStrategyType
	{
		// futures
		CalendarSpread, //SP 
		FxCalendarSpread, //FX 
		ReducedTickCalendarSpread,//RT 
		EquityCalendarSpread,//EQ 
		FuturesButterfly,//BF 
		FuturesCondor,//CF 
		FuturesStrip,//FS 
		IntercommoditySpread,//IS 
		Pack,//PK 
		MonthPack,//MP 
		PackButterfly,//PB 
		DoubleButterfly,//DF 
		PackSpread,//PS 
		Crack1x1,//C1 
		Bundle,//FB 
		BundleSpread,//BS 
		ImpliedTreasuryIntercommoditySpread,//IV 
		TasCalendarSpread,//EC 
		CommoditiesIntercommoditySpread,//SI 
		BmdFuturesStrip,//MS 
		EnergyStrip,//SA 
		BalancedStrip,//SB 
		UnbalancedStripSpread,//WS 
		EnergyIntercommodityStrip,//XS 
		InterestRateIntercommoditySpread,//DI 
		Calendar,//SD 
		InvoiceSwap,//IN 
		TreasuryTailSpread,//TL 
		InvoiceSwapCalendarSpread,//SC 
		InvoiceSwapSwitchSpread, //SW 

		// options
		ThreeWay,//3W 
		ThreeWayStraddleVsCall,//3C 
		ThreeWayStraddleVsPut,//3P 
		Box,//BX 
		OptionsButterfly,//BO 
		XmasTree,//XT 
		ConditionalCurve,//CC 
		OptionsCondor,//CO 
		Double,//DB 
		Horizontal,//HO 
		HorizontalStraddle,//HS 
		IronCondor,//IC 
		Ratio1X2,//12 
		Ratio1x3,//13 
		Ratio2x3,//23 
		RiskReversal,//RR 
		OptionStripSpreads,//GD 
		Straddle,//ST 
		Strangle,//SG 
		OptionsStrip,//SA 
		Vertical,//VT
		JellyRoll,//JR 
		IronButterfly,//IB 
		Guts,//GT 
		Generic,//GN 
		CalendarDiagonal,//DG 
		CoveredOptionOutright,//FO 
		ReducedTickIntercommodityOptionSpread,//EO 
	}
	
}