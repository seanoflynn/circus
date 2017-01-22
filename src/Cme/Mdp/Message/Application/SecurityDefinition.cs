using System;
using System.Collections.Generic;

using Circus.Common;
using Circus.Fix;

namespace Circus.Cme
{	
	public class SecurityDefinition : MdpMessage
	{
		[BitfieldField(1, Tag.MatchEventIndicator, 8)]
		public MatchEventIndicator MatchEventIndicator { get; set; }
		[IntField(2, Tag.TotNumReports)]
		public int? TotNumReports { get; set; }
		[EnumField(3, Tag.SecurityUpdateAction, true)]
		public SecurityUpdateAction SecurityUpdateAction { get; set; }
		[DateTimeField(4, Tag.LastUpdateTime, "yyyyMMddHHmmssffffff")]
		public DateTime LastUpdateTime { get; set; }
		[IntField(5, Tag.ApplID)]
		public int ChannelId { get; set; }
		[StringField(6, Tag.MarketSegmentID)]
		public string MarketSegmentId { get; set; }
		[EnumField(7, Tag.UnderlyingProduct)]
		public UnderlyingAssetClass UnderlyingAssetClass { get; set; }
		[StringField(8, Tag.SecurityExchange)]
		public string Exchange { get; set; }
		[StringField(9, Tag.SecurityGroup)]
		public string Product { get; set; }
		[StringField(10, Tag.Asset)]
		public string Group { get; set; }
		[StringField(11, Tag.Symbol)]
		public string Contract { get; set; }
		[IntField(12, Tag.SecurityID)]
		public int Id { get; set; }
		[EnumField(13, Tag.IDSource)]
		public SecurityIdSource IdSource { get; set; }
		[StringField(14, Tag.SecurityType)]  // longer enum
		public string SecurityType { get; set; }
		[StringField(15, Tag.CfiCode)]
		public string CfiCode { get; set; }
		[EnumField(16, Tag.PutOrCall)]
		public PutOrCall? PutOrCall { get; set; }
		[DateTimeField(17, Tag.MaturityMonthYear, DateTimeFormat.MonthYear)]
		public DateTime MaturityMonth { get; set; }
		[StringField(18, Tag.Currency)]  // longer enum
		public string Currency { get; set; }
		[StringField(19, Tag.SecuritySubType)] // long enum
		public string SecuritySubType { get; set; }
		[DoubleField(20, Tag.StrikePrice)]
		public double? StrikePrice { get; set; }
		[StringField(21, Tag.StrikeCurrency)]  // longer enum
		public String StrikeCurrency { get; set; }
		[DoubleField(22, Tag.MinCabPrice)]
		public double? MinCabPrice { get; set; }
		[EnumField(23, Tag.MatchAlgorithm, true)]
		public MatchAlgorithm MatchAlgorithm { get; set; }
		[IntField(24, Tag.MinTradeVol)]
		public int MinTradeVol { get; set; }
		[IntField(25, Tag.MaxTradeVol)]
		public int MaxTradeVol { get; set; }
		[DoubleField(26, Tag.MinPriceIncrement)]
		public double? MinPriceIncrement { get; set; }
		[DoubleField(27, Tag.MinPriceIncrementAmount)]
		public double? MinPriceIncrementAmount { get; set; }
		[DoubleField(28, Tag.DisplayFactor)]
		public double DisplayFactor { get; set; }
		[StringField(29, Tag.UnitOfMeasure)] // enum by name
		public string UnitOfMeasure { get; set; }
		[DoubleField(30, Tag.UnitOfMeasureQty)]
		public double? UnitOfMeasureQty { get; set; }
		[DoubleField(31, Tag.TradingReferencePrice)]
		public double? TradingReferencePrice { get; set; }
		[BitfieldField(32, Tag.SettlPriceType, 8)]
		public SettlementPriceType? SettlementPriceType { get; set; }
		[DateTimeField(33, Tag.TradingReferenceDate, DateTimeFormat.LocalMktDate)]
		public DateTime? TradingReferenceDate { get; set; }
		[DoubleField(34, Tag.LowLimitPrice)]
		public double? LowLimitPrice { get; set; }
		[DoubleField(35, Tag.HighLimitPrice)]
		public double? HighLimitPrice { get; set; }
		[BoolField(36, Tag.UserDefinedInstrument)]
		public bool? UserDefinedInstrument { get; set; }
		[DoubleField(37, Tag.MaxPriceVariation)]
		public double? MaxPriceVariation { get; set; }
		[DoubleField(42, Tag.ContractMultiplier)]
		public double? ContractMultiplier { get; set; }
		[IntField(66, Tag.OriginalContractSize)]
		public int? OriginalContractSize { get; set; }
		[GroupField(38, Tag.NoEvents)]
		public List<SecurityDefinitionEvent> Events { get; set; } = new List<SecurityDefinitionEvent>();
		[GroupField(39, Tag.NoMdFeedTypes)]
		public List<SecurityDefinitionFeedTypes> MdFeedTypes { get; set; } = new List<SecurityDefinitionFeedTypes>();
		[GroupField(40, Tag.NoInstAttrib)]
		public List<SecurityDefinitionInstrumentAttribute> InstAttribs { get; set; } = new List<SecurityDefinitionInstrumentAttribute>();
		[GroupField(41, Tag.NoLotTypeRules, true)]
		public List<SecurityDefinitionLotTypeRule> LotTypeRules { get; set; } = new List<SecurityDefinitionLotTypeRule>();
		[GroupField(43, Tag.NoLegs)]
		public List<SecurityDefinitionLeg> Legs { get; set; } = new List<SecurityDefinitionLeg>();
		[GroupField(44, Tag.NoUnderlyings)]
		public List<SecurityDefinitionUnderlying> Underlyings { get; set; } = new List<SecurityDefinitionUnderlying>();
		[GroupField(44, Tag.NoRelatedInstruments)]
		public List<SecurityDefinitionRelated> RelatedInstruments { get; set; } = new List<SecurityDefinitionRelated>();
		[IntField(46, Tag.OpenInterestQty)]
		public int? OpenInterest { get; set; }


		[EnumField(19, Tag.MDSecurityTradingStatus)]
		public SecurityTradingStatus? MDSecurityTradingStatus { get; set; }
		[StringField(23, Tag.SettlCurrency)] // longer enum
		public string SettlementCurrency { get; set; }

		[DoubleField(51, Tag.PriceRatio)]
		public double? PriceRatio { get; set; }
		[IntField(52, Tag.TickRule)]
		public int? TickRule { get; set; }
		[IntField(53, Tag.MainFraction)]
		public int? MainFraction { get; set; }
		[IntField(54, Tag.SubFraction)]
		public int? SubFraction { get; set; }
		[IntField(55, Tag.PriceDisplayFormat)]
		public int? PriceDisplayFormat { get; set; }
		[EnumField(59, Tag.ContractMultiplierUnit)]
		public ContractMultiplierUnit? ContractMultiplierUnit { get; set; }
		[EnumField(60, Tag.FlowScheduleType)]
		public FlowScheduleType? FlowScheduleType { get; set; }

		[IntField(64, Tag.DecayQty)]
		public int? DecayQty { get; set; }
		[DateTimeField(65, Tag.DecayStartDate, DateTimeFormat.UtcDate)]
		public DateTime? DecayStartDate { get; set; }


		[IntField(71, Tag.ClearedVolume)]
		public int? ClearedVolume { get; set; }


		public SecurityDefinition() : base(MessageType.SecurityDefinition)
		{ }

		public SecurityDefinition(Security security) : base(MessageType.SecurityDefinition)
		{
			MatchEventIndicator = MatchEventIndicator.LastMessage;
			TotNumReports = 1;
			SecurityUpdateAction = SecurityUpdateAction.Add;
			LastUpdateTime = DateTime.UtcNow;
			//ChannelId = security.ChannelId;

			MarketSegmentId = security.MarketSegmentId;
			Contract = security.Contract;
			Id = security.Id;
			IdSource = security.IdSource;
			MaturityMonth = security.MaturityMonth;
			Product = security.Product;
			Group = security.Group;
			//SecurityType = security.Type;
			//SecuritySubType = security.StrategyType;
			CfiCode = security.CfiCode;
			PutOrCall = security.PutOrCall;
			UnderlyingAssetClass = security.AssetClass;
			Exchange = security.Exchange;
			MDSecurityTradingStatus = security.TradingStatus;
			StrikePrice = security.StrikePrice;
			//StrikeCurrency = security.StrikeCurrency;
			//Currency = security.Currency;
			//SettlementCurrency = security.SettlementCurrency;
			MinCabPrice = security.MinCabPrice;
			UserDefinedInstrument = security.UserDefinedInstrument;

			foreach (var u in security.Underlyings)
			{
				Underlyings.Add(new SecurityDefinitionUnderlying()
				{
					Symbol = u.Symbol,
					SecurityId = u.Id,
					SecurityIdSource = u.IdSource,
				});
			}

			foreach (var l in security.Legs)
			{
				Legs.Add(new SecurityDefinitionLeg()
				{
					LegSecurityID = l.Security.Id,
					LegSide = l.Side,
					LegRatioQty = l.Quantity,
					LegPrice = l.Price,
					//LegOptionDelta = l.OptionDelta,
				});
			}

			foreach (var f in security.FeedTypes)
			{
				MdFeedTypes.Add(new SecurityDefinitionFeedTypes()
				{
					MDFeedType = f.IncludesImplieds ? "GBX" : "GBI",
					MarketDepth = f.Depth,
				});
			}
			if (security.ActivationTime.HasValue)
			{
				Events.Add(new SecurityDefinitionEvent()
				{
					EventType = EventType.Activation,
					EventTime = security.ActivationTime.Value,
				});
			}
			if (security.ExpiryTime.HasValue)
			{
				Events.Add(new SecurityDefinitionEvent()
				{
					EventType = EventType.LastEligibleTradeDate,
					EventTime = security.ExpiryTime.Value,
				});
			}
			MatchAlgorithm = security.MatchAlgorithm;

			foreach (var ltr in security.LotTypeRules)
			{
				LotTypeRules.Add(new SecurityDefinitionLotTypeRule()
				{
					LotType = ltr.LotType,
					MinLotSize = ltr.MinLotSize,
				});
			}
			MinTradeVol = security.MinOrderQuantity;
			MaxTradeVol = security.MaxOrderQuantity;
			MinPriceIncrement = security.TickSize;
			MinPriceIncrementAmount = security.TickValue;
			DisplayFactor = security.DisplayFactor;
			PriceRatio = security.PriceRatio;
			TickRule = security.TickRule;
			MainFraction = security.MainFraction;
			SubFraction = security.SubFraction;
			PriceDisplayFormat = security.PriceDisplayFormat;

			foreach (var ia in security.InstrumentAttributes)
			{
				InstAttribs.Add(new SecurityDefinitionInstrumentAttribute()
				{
					InstAttribType = ia.Type,
					InstAttribValue = ia.Value,
				});
			}

			ContractMultiplierUnit = security.ContractMultiplierUnit;
			FlowScheduleType = security.FlowScheduleType;
			//ContractMultiplier = security.ContractMultiplier;
			//UnitOfMeasure = security.UnitOfMeasure;
			UnitOfMeasureQty = security.UnitOfMeasureQuantity;
			DecayQty = security.DecayQty;
			DecayStartDate = security.DecayStartDate;
			OriginalContractSize = security.OriginalContractSize;

			TradingReferencePrice = security.TradingReferencePrice;
			SettlementPriceType = security.SettlementPriceType;
			TradingReferenceDate = security.TradingReferenceDate;
			OpenInterest = security.OpenInterest;
			ClearedVolume = security.ClearedVolume;
			HighLimitPrice = security.HighLimitPrice;
			LowLimitPrice = security.LowLimitPrice;
			MaxPriceVariation = security.MaxPriceVariation;
		}

		public Security ToSecurity()
		{
			Security sec = new Security()
			{
				CfiCode = CfiCode,
				MarketSegmentId = MarketSegmentId,

				IdSource = IdSource,
				Id = Id,

				Contract = Contract,
				Product = Product,
				Group = Group,
				AssetClass = UnderlyingAssetClass,
				Exchange = Exchange,

				Type = SecurityType == "FUT" ? Common.SecurityType.Future : Common.SecurityType.Option,

				MaturityMonth = MaturityMonth,

				Currency = (Currency)Enum.Parse(typeof(Currency),Currency),
				//SettlementCurrency = (Currency)Enum.Parse(typeof(Currency), SettlementCurrency),

				MatchAlgorithm = MatchAlgorithm,
				MinOrderQuantity = MinTradeVol,
				MaxOrderQuantity = MaxTradeVol,
				TickSize = MinPriceIncrement,
				TickValue = MinPriceIncrementAmount,
				DisplayFactor = DisplayFactor,

				ContractMultiplier = ContractMultiplier,
				UnitOfMeasure = UnitOfMeasure,
				UnitOfMeasureQuantity = UnitOfMeasureQty,

				HighLimitPrice = HighLimitPrice,
				LowLimitPrice = LowLimitPrice,
				MaxPriceVariation = MaxPriceVariation,

				TradingReferenceDate = TradingReferenceDate,
				TradingReferencePrice = TradingReferencePrice,
				SettlementPriceType = SettlementPriceType,
				OpenInterest = OpenInterest,
				ClearedVolume = ClearedVolume,

				PriceDisplayFormat = PriceDisplayFormat,
			};

			foreach (var ev in Events)
			{
				if(ev.EventType == EventType.Activation)
					sec.ActivationTime = ev.EventTime;
				else if(ev.EventType == EventType.LastEligibleTradeDate)
					sec.ExpiryTime = ev.EventTime;
			}

			foreach (var leg in Legs)
			{
				sec.Legs.Add(new Leg()
				{
					Price = leg.LegPrice,
					Quantity = leg.LegRatioQty,
					Side = leg.LegSide,
					//Security = leg.LegSecurityID
				});
			}

			foreach (var feedType in MdFeedTypes)
			{
				sec.FeedTypes.Add(new MarketDataFeedType()
				{
					IncludesImplieds = feedType.MDFeedType == "GBI",
					Depth = feedType.MarketDepth,
				});
			}

			return sec;
		}
	}

	public class SecurityDefinitionUnderlying : Section
	{
		[IntField(1, Tag.UnderlyingSecurityID)]
		public int SecurityId { get; set; }
		[EnumField(2, Tag.UnderlyingIDSource)]
		public SecurityIdSource SecurityIdSource { get; set; }
		[StringField(3, Tag.UnderlyingSymbol)]
		public string Symbol { get; set; }
	}

	public class SecurityDefinitionRelated : Section
	{
		[IntField(1, Tag.RelatedSecurityID)]
		public int SecurityID { get; set; }
		[EnumField(2, Tag.RelatedSecurityIDSource)]
		public SecurityIdSource SecurityIDSource { get; set; }
		[StringField(3, Tag.RelatedSymbol)]
		public string Symbol { get; set; }
	}

	public class SecurityDefinitionLeg : Section
	{
		[IntField(1, Tag.LegSecurityID)]
		public int LegSecurityID { get; set; }
		[IntField(2, Tag.LegSecurityIDSource)]
		public SecurityIdSource LegSecurityIDSource { get; set; }
		[EnumField(3, Tag.LegSide)]
		public Side LegSide { get; set; }
		[IntField(4, Tag.LegRatioQty)]
		public int LegRatioQty { get; set; }
		[DoubleField(5, Tag.LegPrice)]
		public double? LegPrice { get; set; }
		[DoubleField(6, Tag.LegOptionDelta)]
		public double? LegOptionDelta { get; set; }
	}

	public class SecurityDefinitionFeedTypes : Section
	{
		[StringField(1, Tag.MDFeedType)]
		public string MDFeedType { get; set; }
		[IntField(2, Tag.MarketDepth)]
		public int MarketDepth { get; set; }
	}

	public class SecurityDefinitionEvent : Section
	{
		[EnumField(1, Tag.EventType)]
		public EventType EventType { get; set; }
		[DateTimeField(2, Tag.EventTime, "yyyyMMddHHmmssffffff")]
		public DateTime EventTime { get; set; }
	}

	public class SecurityDefinitionLotTypeRule : Section
	{
		[EnumField(1, Tag.LotType)]
		public LotType LotType { get; set; }
		[DoubleField(2, Tag.MinLotSize)]
		public double MinLotSize { get; set; }
	}

	public class SecurityDefinitionInstrumentAttribute : Section
	{
		[EnumField(1, Tag.InstAttribType)]
		public InstrumentAttributeType InstAttribType { get; set; }
		[BitfieldField(2, Tag.InstAttribValue, 32)]
		public InstrumentAttributeValue InstAttribValue { get; set; }
	}

}