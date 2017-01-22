using System;
using System.Net;
using System.Collections.Generic;

namespace Circus.Common
{
	public class Security
	{
		// instrument
		public string CfiCode { get; set; }
		public string MarketSegmentId { get; set; }

		public SecurityIdSource IdSource { get; set; }
		public int Id { get; set; }

		public string Contract { get; set; }					// iLink: SecurityDesc		MDP: Symbol
		public string Product { get; set; }						// iLink: Symbol			MDP: SecurityGroup
		public string Group { get; set; }						// iLink: SecurityGroup		MDP: Asset
		public UnderlyingAssetClass AssetClass { get; set; }
		public string Exchange { get; set; }

		public SecurityType Type { get; set; }

		public DateTime? ActivationTime { get; set; }
		public DateTime? ExpiryTime { get; set; }
		public DateTime MaturityMonth { get; set; }

		public Currency Currency { get; set; }
		public Currency SettlementCurrency { get; set; }

		// spreads
		public List<Leg> Legs { get; } = new List<Leg>();

		// trading rules
		public List<MarketDataFeedType> FeedTypes { get; set; } = new List<MarketDataFeedType>();
		public MatchAlgorithm MatchAlgorithm { get; set; }
		public int MinOrderQuantity { get; set; }
		public int MaxOrderQuantity { get; set; }
		public double? TickSize { get; set; }
		public double? TickValue { get; set; }
		public double DisplayFactor { get; set; }

		// contract lot size/measure/unit
		public double? ContractMultiplier { get; set; }
		public string UnitOfMeasure { get; set; }
		public double? UnitOfMeasureQuantity { get; set; }

		// state and limits
		public SecurityTradingStatus? TradingStatus { get; set; }
		public double? HighLimitPrice { get; set; }
		public double? LowLimitPrice { get; set; }
		public double? MaxPriceVariation { get; set; }

		// statistics
		public DateTime? TradingReferenceDate { get; set; }
		public double? TradingReferencePrice { get; set; }
		public SettlementPriceType? SettlementPriceType { get; set; }
		public int? OpenInterest { get; set; }
		public int? ClearedVolume { get; set; }

		// options/strategy/variable tick size stuff
		public List<LotTypeRule> LotTypeRules { get; } = new List<LotTypeRule>();
		public List<InstrumentAttribute> InstrumentAttributes { get; } = new List<InstrumentAttribute>();
		public bool? UserDefinedInstrument { get; set; }
		public SecurityStrategyType? StrategyType { get; set; }
		public PutOrCall? PutOrCall { get; set; }
		public int? MinCabPrice { get; set; }
		public int? StrikePrice { get; set; }
		public Currency StrikeCurrency { get; set; }
		public List<Underlying> Underlyings { get; } = new List<Underlying>();
		public int? PriceRatio { get; set; }
		public int? TickRule { get; set; }
		public int? MainFraction { get; set; }
		public int? SubFraction { get; set; }
		public int? PriceDisplayFormat { get; set; }
		public ContractMultiplierUnit ContractMultiplierUnit { get; set; }
		public FlowScheduleType FlowScheduleType { get; set; }
		public int? DecayQty { get; set; }
		public DateTime? DecayStartDate { get; set; }
		public int? OriginalContractSize { get; set; }
	}

	public class Leg
	{
		public Security Security { get; set; }
		public Side Side { get; set; }
		public int Quantity { get; set; }
		public double? Price { get; set; }
	}

	public enum MarketDataChannelConnectionType
	{
		HistoricalReplay,
		InstrumentReplay,
		Incremental,
		Snapshot,
		SnapshotMBO,
	}

	public class MarketDataFeedType
	{
		public bool IncludesImplieds { get; set; }
		public int Depth { get; set; }
	}

	public class MarketDataEvent
	{
		public EventType Type { get; set; }
		public DateTime Time { get; set; }
	}

	public class Underlying
	{
		public SecurityIdSource IdSource { get; set; }
		public int Id { get; set; }
		public string Symbol { get; set; }
	}

	public class LotTypeRule
	{
		public LotType LotType { get; set; }
		public int MinLotSize { get; set; }
	}

	public class InstrumentAttribute
	{
		public InstrumentAttributeType Type { get; set; }
		public InstrumentAttributeValue Value { get; set; }
	}
}