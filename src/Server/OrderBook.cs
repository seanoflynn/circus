using System;
using System.Linq;
using System.Collections.Generic;

using Circus.Common;

namespace Circus.Server
{
	public class OrderBook
	{
		public event EventHandler<OrderCreatedEventArgs> OrderCreated;
		public event EventHandler<OrderUpdateEventArgs> OrderUpdated;
		public event EventHandler<OrderDeletedEventArgs> OrderDeleted;

		public event EventHandler<OrderCreateRejectedEventArgs> OrderCreateRejected;
		public event EventHandler<OrderUpdateRejectedEventArgs> OrderUpdateRejected;
		public event EventHandler<OrderDeleteRejectedEventArgs> OrderDeleteRejected;

		public event EventHandler<OrderFilledEventArgs> OrderFilled;
		public event EventHandler<OrderExpiredEventArgs> OrderExpired;

		public event EventHandler<TradedEventArgs> Traded;
		public event EventHandler<BookUpdatedEventArgs> BookUpdated;

		public Security Security { get; private set; }
		public SecurityTradingStatus Status { get; private set; } = SecurityTradingStatus.NotAvailable;

		private List<Order> orders = new List<Order>();

		private int? lastTradePrice;
		private int sessionVolume;
		private int? sessionMaxTradePrice;
		private int? sessionMinTradePrice;
		private int? sessionMaxBidPrice;
		private int? sessionMinAskPrice;
		private int? sessionOpenPrice;

		public DateTime SessionDate { get; private set; } 
		public int? LastTradePrice { get { return lastTradePrice; } }
		public int SessionVolume { get { return sessionVolume; } }
		public int? SessionMaxTradePrice { get { return sessionMaxTradePrice; } }
		public int? SessionMinTradePrice { get { return sessionMinTradePrice; } }
		public int? SessionMaxBidPrice { get { return sessionMaxBidPrice; } }
		public int? SessionMinAskPrice { get { return sessionMinAskPrice; } }
		public int? SessionOpenPrice { get { return sessionOpenPrice; } }

		public DateTime PreviousSessionDate { get; private set; }
		public int PreviousSessionVolume { get; private set; }
		public int PreviousSessionOpenInterest { get; private set; }
		public int PreviousSessionSettlementPrice { get; private set; }

		public OrderBook(Security sec)
		{
			Security = sec;
		}

		public void CreateLimitOrder(int id, TimeInForce tif, DateTime? expiry, Side side,
		                             int? price, int quantity, int minQuantity = 0, 
		                             int maxVisibleQuantity = int.MaxValue, string smpId = null, 
		                             SelfMatchPreventionInstruction smpInstruction = SelfMatchPreventionInstruction.CancelResting)
		{
			var order = new Order(id, Security, OrderType.Limit, tif, expiry, side, price, null,
								  quantity, minQuantity, maxVisibleQuantity,
								  smpId, smpInstruction);

			if (quantity < 1)
			{
				FireCreateRejected(order, OrderRejectReason.QuantityTooLow);
				return;
			}

			if (Status == SecurityTradingStatus.Close || Status == SecurityTradingStatus.NotAvailable)
			{
				FireCreateRejected(order, OrderRejectReason.MarketClosed);
				return;
			}

			if (order.TimeInForce == TimeInForce.FillAndKill)
			{
				// TODO: catch earlier?
				if (order.MinQuantity > order.Quantity)
				{
					FireCreateRejected(order, OrderRejectReason.QuantityOutOfRange);
					return;
				}
			}

			CreateOrder(order);
			Match();
		}

		public void CreateMarketOrder(int id, TimeInForce tif, DateTime? expiry, Side side,
									  int quantity, int minQuantity = 0,
									  int maxVisibleQuantity = int.MaxValue, string smpId = null,
									  SelfMatchPreventionInstruction smpInstruction = SelfMatchPreventionInstruction.CancelResting)
		{
			var order = new Order(id, Security, OrderType.Market, tif, expiry, side, null, null,
								  quantity, minQuantity, maxVisibleQuantity,
								  smpId, smpInstruction);

			if (quantity < 1)
			{
				FireCreateRejected(order, OrderRejectReason.QuantityTooLow);
				return;
			}

			if (Status != SecurityTradingStatus.Open)
			{
				FireCreateRejected(order, OrderRejectReason.MarketClosed);
				return;
			}

			// TODO: reject if for market-limit, "A designated limit is farther than price bands from current Last Best Price"

			var top = WorkingOrders(order.Side == Side.Buy ? Side.Sell : Side.Buy).FirstOrDefault();

			// check if book is empty
			if (top == null)
			{
				FireCreateRejected(order, OrderRejectReason.NoOrdersToMatchMarketOrder);
				return;
			}

			// TODO: need to get these Protection Point values from somewhere
			// 		 -- "Protection point values usually equal half of the Non-reviewable range"
			int protectionPoints = 10 * (order.Side == Side.Buy ? 1 : -1);
			order.Price = top.Price + protectionPoints;

			if (order.TimeInForce == TimeInForce.FillAndKill)
			{
				// TODO: catch earlier?
				if (order.MinQuantity > order.Quantity)
				{
					FireCreateRejected(order, OrderRejectReason.QuantityOutOfRange);
					return;
				}
			}

			CreateOrder(order);
			Match();
		}

		public void CreateMarketLimitOrder(int id, TimeInForce tif, DateTime? expiry, Side side,
										   int quantity, int minQuantity = 0,
									 	   int maxVisibleQuantity = int.MaxValue, string smpId = null,
									 	   SelfMatchPreventionInstruction smpInstruction = SelfMatchPreventionInstruction.CancelResting)
		{
			var order = new Order(id, Security, OrderType.MarketLimit, tif, expiry, side, null, null,
								  quantity, minQuantity, maxVisibleQuantity,
								  smpId, smpInstruction);

			if (quantity < 1)
			{
				FireCreateRejected(order, OrderRejectReason.QuantityTooLow);
				return;
			}

			if (Status != SecurityTradingStatus.Open)
			{
				FireCreateRejected(order, OrderRejectReason.MarketClosed);
				return;
			}


			// TODO: reject if for market-limit, "A designated limit is farther than price bands from current Last Best Price"

			var top = WorkingOrders(order.Side == Side.Buy ? Side.Sell : Side.Buy).FirstOrDefault();

			// check if book is empty
			if (top == null)
			{
				FireCreateRejected(order, OrderRejectReason.NoOrdersToMatchMarketOrder);
				return;
			}

			order.Price = top.Price;

			if (order.TimeInForce == TimeInForce.FillAndKill)
			{
				// TODO: catch earlier?
				if (order.MinQuantity > order.Quantity)
				{
					FireCreateRejected(order, OrderRejectReason.QuantityOutOfRange);
					return;
				}
			}

			CreateOrder(order);
			Match();
		}

		public void CreateStopOrder(int id, TimeInForce tif, DateTime? expiry, Side side,
									int? stopPrice, int quantity, int minQuantity = 0,
									int maxVisibleQuantity = int.MaxValue, string smpId = null,
									SelfMatchPreventionInstruction smpInstruction = SelfMatchPreventionInstruction.CancelResting)
		{
			var order = new Order(id, Security, OrderType.Stop, tif, expiry, side, null, stopPrice,
								  quantity, minQuantity, maxVisibleQuantity,
								  smpId, smpInstruction);

			if (quantity < 1)
			{
				FireCreateRejected(order, OrderRejectReason.QuantityTooLow);
				return;
			}

			if (Status == SecurityTradingStatus.Close || Status == SecurityTradingStatus.NotAvailable)
			{
				FireCreateRejected(order, OrderRejectReason.MarketClosed);
				return;
			}

			if (order.Side == Side.Buy && stopPrice.Value > lastTradePrice)
			{
				FireCreateRejected(order, OrderRejectReason.StopPriceMustBeLessThanLastTradePrice);
				return;
			}
			if (order.Side == Side.Sell && stopPrice.Value < lastTradePrice)
			{
				FireCreateRejected(order, OrderRejectReason.StopPriceMustBeGreaterThanLastTradePrice);
				return;
			}

			if (order.TimeInForce == TimeInForce.FillAndKill)
			{
				// TODO: catch earlier?
				if (order.MinQuantity > order.Quantity)
				{
					FireCreateRejected(order, OrderRejectReason.QuantityOutOfRange);
					return;
				}
			}

			CreateOrder(order);
			order.Status = OrderStatus.Hidden;
			Match();
		}

		public void CreateStopLimitOrder(int id, TimeInForce tif, DateTime? expiry, Side side,
										 int? price, int? stopPrice, int quantity, int minQuantity = 0,
									 	 int maxVisibleQuantity = int.MaxValue, string smpId = null,
									 	 SelfMatchPreventionInstruction smpInstruction = SelfMatchPreventionInstruction.CancelResting)
		{
			var order = new Order(id, Security, OrderType.StopLimit, tif, expiry, side, price, stopPrice,
								  quantity, minQuantity, maxVisibleQuantity,
								  smpId, smpInstruction);

			if (quantity < 1)
			{
				FireCreateRejected(order, OrderRejectReason.QuantityTooLow);
				return;
			}

			if (Status == SecurityTradingStatus.Close || Status == SecurityTradingStatus.NotAvailable)
			{
				FireCreateRejected(order, OrderRejectReason.MarketClosed);
				return;
			}

			if (order.Side == Side.Buy && stopPrice.Value > lastTradePrice)
			{
				FireCreateRejected(order, OrderRejectReason.StopPriceMustBeLessThanLastTradePrice);
				return;
			}
			if (order.Side == Side.Sell && stopPrice.Value < lastTradePrice)
			{
				FireCreateRejected(order, OrderRejectReason.StopPriceMustBeGreaterThanLastTradePrice);
				return;
			}

			if (order.Type == OrderType.StopLimit)
			{
				if (order.Side == Side.Buy && price.Value < stopPrice.Value)
				{
					FireCreateRejected(order, OrderRejectReason.InvalidStopPriceMustBeGreaterThanEqualTriggerPrice);
					return;
				}
				if (order.Side == Side.Sell && price.Value > stopPrice.Value)
				{
					FireCreateRejected(order, OrderRejectReason.InvalidStopPriceMustBeLessThanEqualTriggerPrice);
					return;
				}
			}

			if (order.TimeInForce == TimeInForce.FillAndKill)
			{
				// TODO: catch earlier?
				if (order.MinQuantity > order.Quantity)
				{
					FireCreateRejected(order, OrderRejectReason.QuantityOutOfRange);
					return;
				}
			}

			CreateOrder(order);
			order.Status = OrderStatus.Hidden;
			Match();
		}


		public void UpdateLimitOrder(int id, int? price, int quantity, int maxVisibleQuantity = int.MaxValue)
		{
			Order order = orders.Find(x => x.Id == id);

			if (Status == SecurityTradingStatus.Close || Status == SecurityTradingStatus.NotAvailable)
			{
				FireCreateRejected(order, OrderRejectReason.MarketClosed);
				return;
			}

			if (order.Status == OrderStatus.Completed)
			{
				FireUpdateRejected(order, CancelRejectReason.TooLateToCancel);
				return;
			}

			if (quantity < 1)
			{
				FireCreateRejected(order, OrderRejectReason.QuantityTooLow);
				return;
			}

			// validate

			if (price != order.Price || quantity > order.Quantity)
				order.Time = DateTime.UtcNow;

			order.Price = price.Value;
			order.Quantity = quantity;
			// TODO: in-flight handling?
			order.RemainingQuantity = order.Quantity - order.FilledQuantity;			     
			order.MaxVisibleQuantity = maxVisibleQuantity;

			UpdateOrder(order);

			// TODO: only if price changes
			Match();
		}

		public void DeleteOrder(int id)
		{
			Order order = orders.Find(x => x.Id == id);

			// TODO: not available should be session level reject
			if (Status == SecurityTradingStatus.Close || Status == SecurityTradingStatus.NotAvailable)
			{
				FireCreateRejected(order, OrderRejectReason.MarketClosed);
				return;
			}

			if (Status == SecurityTradingStatus.NewPriceIndication)
			{
				FireCreateRejected(order, OrderRejectReason.MarketNoCancel);
				return;
			}

			if (order.Status == OrderStatus.Completed)
			{
				FireUpdateRejected(order, CancelRejectReason.TooLateToCancel);
				return;
			}

			// validate

			DeleteOrder(order);
		}

		private bool IsWorking(Side side)
		{
			return orders.Any(x => (x.Status & OrderStatus.Working) != 0 && x.Side == side);
		}

		public bool Contains(int id)
		{
			return orders.Any(x => x.Id == id);
		}

		private IEnumerable<Order> StopOrders()
		{
			return orders.Where(x => (x.Status & OrderStatus.Hidden) != 0);
		}

		private IEnumerable<Order> WorkingOrders()
		{
			return orders.Where(x => (x.Status & OrderStatus.Working) != 0)
						 .OrderBy(x => x.Time)
						 .OrderByDescending(x => x.Price * (x.Side == Side.Buy ? 1 : -1));
		}

		private IEnumerable<Order> WorkingOrders(Side side)
		{
			return orders.Where(x => (x.Status & OrderStatus.Working) != 0 && x.Side == side)
							  .OrderBy(x => x.Time)
							  .OrderByDescending(x => x.Price * (x.Side == Side.Buy ? 1 : -1));
		}

		private void MatchOnOpen()
		{
			// special matching procedure
		}

		private void Match()
		{
			if (Status != SecurityTradingStatus.Open)
				return;

			var fills = new List<Fill>();
			var time = DateTime.UtcNow;

			int maxTradePrice = int.MinValue;
			int minTradePrice = int.MaxValue;

			var buy = WorkingOrders(Side.Buy).FirstOrDefault();
			var sell = WorkingOrders(Side.Sell).FirstOrDefault();

			while (buy != null && sell != null && buy.Price >= sell.Price)
			{
				Order resting = buy.Id < sell.Id ? buy : sell;
				Order aggressor = buy == resting ? sell : buy;

				fills.AddRange(Match(resting, aggressor));

				maxTradePrice = Math.Max(maxTradePrice, resting.Price.Value);
				minTradePrice = Math.Min(minTradePrice, resting.Price.Value);

				buy = WorkingOrders(Side.Buy).FirstOrDefault();
				sell = WorkingOrders(Side.Sell).FirstOrDefault();
			}

			// remove FAK orders (should only ever be one)
			foreach (var order in WorkingOrders())
			{
				if (order.TimeInForce == TimeInForce.FillAndKill)
				{
					ExpireOrder(order);
					Console.WriteLine($"FAK expired: {order.Id}");
				}
			}

			CheckStops(maxTradePrice, minTradePrice);

			if (fills.Count > 0)
			{
                Traded?.Invoke(this, new TradedEventArgs(time, Security, fills));
            }
		}

		private void CheckStops(int maxTradePrice, int minTradePrice)
		{
			// check stops
			bool triggered = false;
			foreach (var order in StopOrders())
			{
				if ((order.Side == Side.Buy && maxTradePrice < order.StopPrice) ||
					(order.Side == Side.Sell && minTradePrice > order.StopPrice))
				{
					continue;
				}

				Console.WriteLine($"stop order triggered: {order.Id}");
				order.Status = OrderStatus.Created;
				order.Time = DateTime.UtcNow;

				// set protected limit
				if (order.Type == OrderType.Stop)
				{
					var top = WorkingOrders(order.Side == Side.Buy ? Side.Sell : Side.Buy).FirstOrDefault();

					// TODO: what to do if book is empty?
					if (top == null)
					{
						throw new NotImplementedException("no orders in market after stop triggered, unable to set a protected price");
					}

					int protectionPoints = 10 * (order.Side == Side.Buy ? 1 : -1);

					order.Price = top.Price + protectionPoints;
				}

				triggered = true;
			}

			if (triggered)
				Match();
		}

		private List<Fill> Match(Order resting, Order aggressing)
		{
			// self match prevention
			/*if (resting.SmpId == aggressing.SmpId)
			{
				var ordToCancel = resting.SmpInstruction == SelfMatchPreventionInstruction.CancelAggressing ? aggressing : resting;
				ExecRestatementReason reason = resting.SmpInstruction == SelfMatchPreventionInstruction.CancelAggressing ? ExecRestatementReason.SelfMatchPreventionAggressing : ExecRestatementReason.SelfMatchPreventionResting;
				ordToCancel.Status = OrderStatus.Cancelled;
				FireDeleteAccepted(ordToCancel, ordToCancel.LastRequest, reason);
				//ordToCancel.Client.Send(new CancelAck(ordToCancel.LastRequest, ordToCancel.Security, ordToCancel.FilledQuantity, reason));
				
				Console.WriteLine($"self match prevention, cancelling {ordToCancel.Id}");
				return new List<Fill>();
			}*/

			DateTime time = DateTime.UtcNow;
			int quantity = Math.Min(resting.RemainingQuantity, aggressing.RemainingQuantity);
			int price = resting.Price.Value;

			// min quantity
			if (quantity < aggressing.MinQuantity)
			{
				ExpireOrder(aggressing);
				return new List<Fill>();
			}

			FillOrder(resting, time, price, quantity, false);
			FillOrder(aggressing, time, price, quantity, true);

			lastTradePrice = price;
			sessionVolume += quantity;
			sessionMaxTradePrice = sessionMaxTradePrice.HasValue ? Math.Max(sessionMaxTradePrice.Value, price) : price;
			sessionMinTradePrice = sessionMinTradePrice.HasValue ? Math.Min(sessionMinTradePrice.Value, price) : price;
			if (!sessionOpenPrice.HasValue)
				sessionOpenPrice = price;

			return new List<Fill>() { 
				new Fill(aggressing.Id, aggressing.Side, price, quantity, true),
				new Fill(resting.Id, resting.Side, price, quantity, false),
			};
		}

		public void SetStatus(SecurityTradingStatus status)
		{
			if (status == SecurityTradingStatus.Close)
			{
				ExpireAllOrders();
			}
			else if (status == SecurityTradingStatus.Open && Status == SecurityTradingStatus.NewPriceIndication)
			{
				MatchOnOpen();
			}

			Status = status;

			Match();
		}

		private void ExpireAllOrders()
		{
			foreach (var order in orders)
			{
				if (order.TimeInForce == TimeInForce.GoodTilCancel ||
				   order.TimeInForce == TimeInForce.GoodTilDate)
					continue;

				ExpireOrder(order);
			}
		}

		#region Orders

		private void CreateOrder(Order order)
		{
			order.Status = OrderStatus.Created;
			order.Time = DateTime.UtcNow;

			orders.Add(order);

			Console.WriteLine($"order added to book: {order}");

			var bid = WorkingOrders(Side.Buy).FirstOrDefault();
			if (bid != null && bid.Price > sessionMaxBidPrice)
				sessionMaxBidPrice = bid.Price;
			
			var ask = WorkingOrders(Side.Sell).FirstOrDefault();
			if (ask != null && ask.Price > sessionMinAskPrice)
				sessionMinAskPrice = ask.Price;

            OrderCreated?.Invoke(this, new OrderCreatedEventArgs(order));
        }

		private void UpdateOrder(Order order)
		{
			order.Status = OrderStatus.Updated;

			Console.WriteLine($"order updated in book: {order}");

            OrderUpdated?.Invoke(this, new OrderUpdateEventArgs(order));
        }

		private void DeleteOrder(Order order, ExecRestatementReason? reason = null)
		{
			order.RemainingQuantity = 0;
			order.Status = OrderStatus.Deleted;

			Console.WriteLine($"order deleted from book: {order}");

            OrderDeleted?.Invoke(this, new OrderDeletedEventArgs(order, reason));
        }

		private void FireCreateRejected(Order order, OrderRejectReason reason)
		{
            OrderCreateRejected?.Invoke(this, new OrderCreateRejectedEventArgs(order, reason));
        }

		private void FireUpdateRejected(Order order, CancelRejectReason reason)
		{
            OrderUpdateRejected?.Invoke(this, new OrderUpdateRejectedEventArgs(order, reason));
        }

		private void FireDeleteRejected(Order order, CancelRejectReason reason)
		{
            OrderDeleteRejected?.Invoke(this, new OrderDeleteRejectedEventArgs(order, reason));
        }

		private void ExpireOrder(Order order)
		{
			order.RemainingQuantity = 0;
			order.Status = OrderStatus.Expired;

            OrderExpired?.Invoke(this, new OrderExpiredEventArgs(order));
        }

		private void FillOrder(Order order, DateTime time, int price, int quantity, bool isAggressor)
		{
			order.FilledQuantity += quantity;
			order.RemainingQuantity -= quantity;
			order.Status = order.RemainingQuantity == 0 ? OrderStatus.Filled : OrderStatus.PartiallyFilled;

            OrderFilled?.Invoke(this, new OrderFilledEventArgs(order, time, price, quantity, isAggressor));
        }

		#endregion

		public AggregateBook GetAggregateBook(int depth)
		{
			var bids = WorkingOrders(Side.Buy).GroupBy(x => x.Price)
											  .Take(depth)
											  .Select(x => new AggregateBookLevel(x.Key.Value, x.Sum(y => y.Quantity), x.Count()));

			var asks = WorkingOrders(Side.Sell).GroupBy(x => x.Price)
											   .Take(depth)
											   .Select(x => new AggregateBookLevel(x.Key.Value, x.Sum(y => y.Quantity), x.Count()));

			var ab = new AggregateBook(depth, bids, asks);
			  
			return ab;
		}
	}

	public class AggregateBook
	{
		public AggregateBookLevel[] Bids { get; private set; }
		public AggregateBookLevel[] Asks { get; private set; }
		public int Depth { get; private set; }

		public AggregateBook(int depth)
		{
			Depth = depth;
			Bids = new AggregateBookLevel[depth];
			Asks = new AggregateBookLevel[depth];
		}

		public AggregateBook(int depth, IEnumerable<AggregateBookLevel> bids, IEnumerable<AggregateBookLevel> asks)
		{
			Depth = depth;
			Bids = new AggregateBookLevel[depth];
			Asks = new AggregateBookLevel[depth];

			var b = bids.ToArray();
			var a = asks.ToArray();

			for (int i = 0; i < 10; i++)
			{
				if (i < b.Length)
					Bids[i] = b[i];

				if (i < a.Length)
					Asks[i] = a[i];
			}
		}
	}

	public class AggregateBookLevel
	{
		public int Price { get; set; }
		public int Quantity { get; set; }
		public int Count { get; set; }

		public AggregateBookLevel(int price, int quantity, int count)
		{
			Price = price;
			Quantity = quantity;
			Count = count;
		}
	}
}