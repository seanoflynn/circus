using System;
using System.Collections.Generic;

using Circus.Common;
using Circus.Fix;

namespace Circus.Cme
{
	public class MarketDataUpdateDataBlock : Section
	{
		[EnumField(4, Tag.MDUpdateAction)]
		public MDUpdateAction? Action { get; set; }
		[EnumField(5, Tag.MDEntryType, true)]
		public MDEntryType Type { get; set; }
		[IntField(6, Tag.SecurityID)]
		public int? SecurityId { get; set; }
		[IntField(7, Tag.RptSeq)]
		public int? RptSeq { get; set; }
		[IntField(8, Tag.MDEntryPx)]
		public int? Price { get; set; }
		[IntField(9, Tag.MDEntrySize)]
		public int? Quantity { get; set; }
		[IntField(10, Tag.NumberOfOrders)]
		public int? NumberOfOrders { get; set; }
		[IntField(11, Tag.MDPriceLevel)]
		public int? Level { get; set; }
		[EnumField(12, Tag.OpenCloseSettleFlag)]
		public OpenCloseSettleFlag? OpenCloseSettleFlag { get; set; }
		[EnumField(13, Tag.SettlPriceType)]
		public SettlementPriceType? SettlPriceType { get; set; }
		[EnumField(14, Tag.AggressorSide)]
		public Side? AggressorSide { get; set; }
		[DateTimeField(15, Tag.TradingReferenceDate, DateTimeFormat.LocalMktDate)]
		public DateTime? TradingReferenceDate { get; set; }
		[DoubleField(16, Tag.HighLimitPrice)]
		public double? HighLimitPrice { get; set; }
		[DoubleField(17, Tag.LowLimitPrice)]
		public double? LowLimitPrice { get; set; }
		[DoubleField(18, Tag.MaxPriceVariation)]
		public double? MaxPriceVariation { get; set; }
		[IntField(19, Tag.ApplID)]
		public int? ChannelId { get; set; }

		public static MarketDataUpdateDataBlock RealBookNew(Security security, Side side, int seqNum, int price, int quantity, int level, int numberOfOrders)
		{
			return new MarketDataUpdateDataBlock()
			{
				Action = MDUpdateAction.New,
				Type = side == Side.Buy ? MDEntryType.Bid : MDEntryType.Offer,
				SecurityId = security.Id,
				RptSeq = seqNum,
				Price = price,
				Quantity = quantity,
				Level = level,
				NumberOfOrders = numberOfOrders,
			};
		}

		public static MarketDataUpdateDataBlock RealBookUpdate(Security security, Side side, int seqNum, int price, int quantity, int level, int numberOfOrders)
		{
			return new MarketDataUpdateDataBlock()
			{
				Action = MDUpdateAction.Change,
				Type = side == Side.Buy ? MDEntryType.Bid : MDEntryType.Offer,
				SecurityId = security.Id,
				RptSeq = seqNum,
				Price = price,
				Quantity = quantity,
				Level = level,
				NumberOfOrders = numberOfOrders,
			};
		}

		public static MarketDataUpdateDataBlock RealBookDelete(Security security, Side side, int seqNum, int price, int quantity, int level, int numberOfOrders)
		{
			return new MarketDataUpdateDataBlock()
			{
				Action = MDUpdateAction.Delete,
				Type = side == Side.Buy ? MDEntryType.Bid : MDEntryType.Offer,
				SecurityId = security.Id,
				RptSeq = seqNum,
				Price = price,
				Quantity = quantity,
				Level = level,
				NumberOfOrders = numberOfOrders,
			};
		}

		public static MarketDataUpdateDataBlock RealBookClear(Security security, Side side, int seqNum, int level)
		{
			return new MarketDataUpdateDataBlock()
			{
				Action = level == 1 ? MDUpdateAction.DeleteThru : MDUpdateAction.DeleteFrom,
				Type = side == Side.Buy ? MDEntryType.Bid : MDEntryType.Offer,
				SecurityId = security.Id,
				RptSeq = seqNum,
				Price = 0,
				Quantity = 0,
				Level = level,
				NumberOfOrders = 0,
			};
		}

		public static MarketDataUpdateDataBlock ImpliedBookNew(Security security, Side side, int seqNum, int price, int quantity, int level, int numberOfOrders)
		{
			return new MarketDataUpdateDataBlock()
			{
				Action = MDUpdateAction.New,
				Type = side == Side.Buy ? MDEntryType.ImpliedBid : MDEntryType.ImpliedOffer,
				SecurityId = security.Id,
				RptSeq = seqNum,
				Price = price,
				Quantity = quantity,
				Level = level,
			};
		}

		public static MarketDataUpdateDataBlock ImpliedBookUpdate(Security security, Side side, int seqNum, int price, int quantity, int level, int numberOfOrders)
		{
			return new MarketDataUpdateDataBlock()
			{
				Action = MDUpdateAction.Change,
				Type = side == Side.Buy ? MDEntryType.ImpliedBid : MDEntryType.ImpliedOffer,
				SecurityId = security.Id,
				RptSeq = seqNum,
				Price = price,
				Quantity = quantity,
				Level = level,
			};
		}

		public static MarketDataUpdateDataBlock ImpliedBookDelete(Security security, Side side, int seqNum, int price, int quantity, int level, int numberOfOrders)
		{
			return new MarketDataUpdateDataBlock()
			{
				Action = MDUpdateAction.Delete,
				Type = side == Side.Buy ? MDEntryType.ImpliedBid : MDEntryType.ImpliedOffer,
				SecurityId = security.Id,
				RptSeq = seqNum,
				Price = price,
				Quantity = quantity,
				Level = level,
			};
		}

		public static MarketDataUpdateDataBlock ChannelReset(int channelId)
		{
			return new MarketDataUpdateDataBlock()
			{
				Action = MDUpdateAction.New,
				Type = MDEntryType.EmptyBook,
				ChannelId = channelId
			};
		}

		public static MarketDataUpdateDataBlock TradeNew(Security security, int seqNum, int price, int quantity, int numberOfOrders, Side aggressorSide)
		{
			return new MarketDataUpdateDataBlock()
			{
				Action = MDUpdateAction.New,
				Type = MDEntryType.TradeSummary,
				SecurityId = security.Id,
				RptSeq = seqNum,
				Price = price,
				Quantity = quantity,
				NumberOfOrders = numberOfOrders,
				AggressorSide = aggressorSide,
				// TODO: Order Entries
			};
		}

		public static MarketDataUpdateDataBlock TradeUpdate(Security security, int seqNum, int price, int quantity)
		{
			return new MarketDataUpdateDataBlock()
			{
				Action = MDUpdateAction.Change,
				Type = MDEntryType.TradeSummary,
				SecurityId = security.Id,
				RptSeq = seqNum,
				Price = price,
				Quantity = quantity,
				NumberOfOrders = 0,
				AggressorSide = Side.Undefined,
			};
		}

		public static MarketDataUpdateDataBlock TradeDelete(Security security, int seqNum, int price, int quantity)
		{
			return new MarketDataUpdateDataBlock()
			{
				Action = MDUpdateAction.Delete,
				Type = MDEntryType.TradeSummary,
				SecurityId = security.Id,
				RptSeq = seqNum,
				Price = price,
				Quantity = quantity,
				NumberOfOrders = 0,
				AggressorSide = Side.Undefined,
			};
		}

		public static MarketDataUpdateDataBlock VolumeNew(Security security, int seqNum, int quantity)
		{
			return new MarketDataUpdateDataBlock()
			{
				Action = MDUpdateAction.New,
				Type = MDEntryType.ElectronicVolume,
				SecurityId = security.Id,
				RptSeq = seqNum,
				Quantity = quantity,
			};
		}

		public static MarketDataUpdateDataBlock VolumeUpdate(Security security, int seqNum, int quantity)
		{
			return new MarketDataUpdateDataBlock()
			{
				Action = MDUpdateAction.Change,
				Type = MDEntryType.ElectronicVolume,
				SecurityId = security.Id,
				RptSeq = seqNum,
				Quantity = quantity,
			};
		}

		public static MarketDataUpdateDataBlock TradeHighLowNew(Security security, int seqNum, bool isHigh, int price)
		{
			return new MarketDataUpdateDataBlock()
			{
				Action = MDUpdateAction.New,
				Type = isHigh ? MDEntryType.TradingSessionHighPrice : MDEntryType.TradingSessionLowPrice,
				SecurityId = security.Id,
				RptSeq = seqNum,
				Price = price
			};
		}

		public static MarketDataUpdateDataBlock TradeHighLowDelete(Security security, int seqNum, bool isHigh, int price)
		{
			return new MarketDataUpdateDataBlock()
			{
				Action = MDUpdateAction.Delete,
				Type = isHigh ? MDEntryType.TradingSessionHighPrice : MDEntryType.TradingSessionLowPrice,
				SecurityId = security.Id,
				RptSeq = seqNum,
				Price = price
			};
		}

		public static MarketDataUpdateDataBlock TradeHighBidLowAskNew(Security security, int seqNum, Side side, int price)
		{
			return new MarketDataUpdateDataBlock()
			{
				Action = MDUpdateAction.New,
				Type = side == Side.Buy ? MDEntryType.SessionHighBid : MDEntryType.SessionLowOffer,
				SecurityId = security.Id,
				RptSeq = seqNum,
				Price = price
			};
		}

		public static MarketDataUpdateDataBlock OpenPriceNew(Security security, int seqNum, int price, bool isIndicative)
		{
			return new MarketDataUpdateDataBlock()
			{
				Action = MDUpdateAction.New,
				Type = MDEntryType.OpeningPrice,
				SecurityId = security.Id,
				RptSeq = seqNum,
				Price = price,
				OpenCloseSettleFlag = isIndicative ? Common.OpenCloseSettleFlag.IndicativeOpeningPrice : Common.OpenCloseSettleFlag.DailyOpenPrice
			};
		}

		public static MarketDataUpdateDataBlock SettlePriceNew(Security security, int seqNum, int price, SettlementPriceType type, DateTime date)
		{
			return new MarketDataUpdateDataBlock()
			{
				Action = MDUpdateAction.New,
				Type = MDEntryType.SettlementPrice,
				SecurityId = security.Id,
				RptSeq = seqNum,
				Price = price,
				SettlPriceType = type,
				TradingReferenceDate = date
			};
		}

		public static MarketDataUpdateDataBlock ClearedVolumeNew(Security security, int seqNum, int quantity, DateTime date)
		{
			return new MarketDataUpdateDataBlock()
			{
				Action = MDUpdateAction.New,
				Type = MDEntryType.TradeVolume,
				SecurityId = security.Id,
				RptSeq = seqNum,
				Quantity = quantity,
				TradingReferenceDate = date
			};
		}

		public static MarketDataUpdateDataBlock OpenInterestNew(Security security, int seqNum, int quantity, DateTime date)
		{
			return new MarketDataUpdateDataBlock()
			{
				Action = MDUpdateAction.New,
				Type = MDEntryType.OpenInterest,
				SecurityId = security.Id,
				RptSeq = seqNum,
				Quantity = quantity,
				TradingReferenceDate = date
			};
		}

		public static MarketDataUpdateDataBlock FixingPriceNew(Security security, int seqNum, int price, DateTime date)
		{
			return new MarketDataUpdateDataBlock()
			{
				Action = MDUpdateAction.New,
				Type = MDEntryType.FixingPrice,
				SecurityId = security.Id,
				RptSeq = seqNum,
				Price = price,
				TradingReferenceDate = date,
			};
		}

		public static MarketDataUpdateDataBlock LimitsNew(Security security, int seqNum, double highLimitPrice, double lowLimitPrice, double maxPriceVariation)
		{
			return new MarketDataUpdateDataBlock()
			{
				Action = MDUpdateAction.New,
				Type = MDEntryType.Limits,
				SecurityId = security.Id,
				RptSeq = seqNum,
				HighLimitPrice = highLimitPrice,
				LowLimitPrice= lowLimitPrice,
				MaxPriceVariation = maxPriceVariation,
			};
		}
	}

	public class IncrementalUpdateOrderEntry : Section
	{
		[StringField(1, Tag.OrderID)]
		public string OrderId { get; set; }
		[IntField(2, Tag.LastQty)]
		public int FillQuantity { get; set; }
	}
}
