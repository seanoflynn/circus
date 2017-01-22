using System;
using System.Diagnostics;

using Circus.Common;
using Circus.Server;

namespace Tests.Server
{
	public class OrderBookTest
	{
		public OrderBookTest()
		{
			LimitOrder();
			//MarketOrder();
			//MarketLimitOrder();
			//StopOrder();
			//StopLimitOrder();
			OrderFilled();
			OrderExpired();
			OrderTraded();
		}

		public void LimitOrder()
		{
			var sec = new Security() { Id = 1, Type = SecurityType.Future, Group = "GC", Product = "GC", Contract = "GCZ6" };
			var book = new OrderBook(sec);
			book.SetStatus(SecurityTradingStatus.Open);

			OrderCreatedEventArgs createdArgs = null;
			book.OrderCreated += (sender, e) => { createdArgs = e; };

			book.CreateLimitOrder(1, TimeInForce.Day, null, Side.Buy, 100, 3);

			Debug.Assert(createdArgs != null);
			Debug.Assert(createdArgs.Order.Status == OrderStatus.Created);
			Debug.Assert(createdArgs.Order.Id == 1);
			Debug.Assert(createdArgs.Order.Price == 100);
			Debug.Assert(createdArgs.Order.Quantity == 3);
			Debug.Assert(createdArgs.Order.FilledQuantity == 0);
			Debug.Assert(createdArgs.Order.RemainingQuantity == 3);
			Debug.Assert(createdArgs.Order.Side == Side.Buy);
			Debug.Assert(createdArgs.Order.Type == OrderType.Limit);
			Debug.Assert(createdArgs.Order.TimeInForce == TimeInForce.Day);
			Debug.Assert(createdArgs.Order.Security == sec);
		
			OrderUpdateEventArgs updatedArgs = null;
			book.OrderUpdated += (sender, e) => { updatedArgs = e; };

			book.UpdateLimitOrder(1, 105, 5);

			Debug.Assert(updatedArgs != null);
			Debug.Assert(updatedArgs.Order.Status == OrderStatus.Updated);
			Debug.Assert(updatedArgs.Order.Id == 1);
			Debug.Assert(updatedArgs.Order.Price == 105);
			Debug.Assert(updatedArgs.Order.Quantity == 5);
			Debug.Assert(updatedArgs.Order.FilledQuantity == 0);
			Debug.Assert(updatedArgs.Order.RemainingQuantity == 5);

			OrderDeletedEventArgs deletedArgs = null;
			book.OrderDeleted += (sender, e) => { deletedArgs = e; };

			book.DeleteOrder(1);

			Debug.Assert(deletedArgs != null);
			Debug.Assert(deletedArgs.Order.Status == OrderStatus.Deleted);
			Debug.Assert(deletedArgs.Order.Id == 1);
			Debug.Assert(deletedArgs.Order.Price == 105);
			Debug.Assert(updatedArgs.Order.Quantity == 5);
			Debug.Assert(deletedArgs.Order.FilledQuantity == 0);
			Debug.Assert(deletedArgs.Order.RemainingQuantity == 0);
			Debug.Assert(deletedArgs.Reason == null);
		}

		public void OrderFilled()
		{
			var sec = new Security() { Id = 1, Type = SecurityType.Future, Group = "GC", Product = "GC", Contract = "GCZ6" };
			var book = new OrderBook(sec);
			book.SetStatus(SecurityTradingStatus.Open);

			OrderFilledEventArgs order1FilledArgs = null;
			OrderFilledEventArgs order2FilledArgs = null;
			book.OrderFilled += (sender, e) => { if (e.Order.Id == 1) order1FilledArgs = e; };
			book.OrderFilled += (sender, e) => { if (e.Order.Id == 2) order2FilledArgs = e; };

			book.CreateLimitOrder(1, TimeInForce.Day, null, Side.Buy, 100, 3);
			book.CreateLimitOrder(2, TimeInForce.Day, null, Side.Sell, 100, 5);

			Debug.Assert(order1FilledArgs != null);
			Debug.Assert(order1FilledArgs.Price == 100);
			Debug.Assert(order1FilledArgs.IsAggressor == false);
			Debug.Assert(order1FilledArgs.Quantity == 3);
			Debug.Assert(order1FilledArgs.Order.Id == 1);
			Debug.Assert(order1FilledArgs.Order.Status == OrderStatus.Filled);
			Debug.Assert(order1FilledArgs.Order.FilledQuantity == 3);
			Debug.Assert(order1FilledArgs.Order.RemainingQuantity == 0);

			Debug.Assert(order2FilledArgs != null);
			Debug.Assert(order2FilledArgs.Price == 100);
			Debug.Assert(order2FilledArgs.IsAggressor == true);
			Debug.Assert(order2FilledArgs.Quantity == 3);
			Debug.Assert(order2FilledArgs.Order.Id == 2);
			Debug.Assert(order2FilledArgs.Order.Status == OrderStatus.PartiallyFilled);
			Debug.Assert(order2FilledArgs.Order.FilledQuantity == 3);
			Debug.Assert(order2FilledArgs.Order.RemainingQuantity == 2);
		}

		public void OrderExpired()
		{
			var sec = new Security() { Id = 1, Type = SecurityType.Future, Group = "GC", Product = "GC", Contract = "GCZ6" };
			var book = new OrderBook(sec);
			book.SetStatus(SecurityTradingStatus.Open);

			OrderExpiredEventArgs args = null;
			book.OrderExpired += (sender, e) => { args = e; };

			book.CreateLimitOrder(2, TimeInForce.Day, null, Side.Buy, 100, 3);
			book.SetStatus(SecurityTradingStatus.Close);

			Debug.Assert(args != null);
			Debug.Assert(args.Order.Status == OrderStatus.Expired);
			Debug.Assert(args.Order.Id == 2);
			Debug.Assert(args.Order.Price == 100);
			Debug.Assert(args.Order.FilledQuantity == 0);
			Debug.Assert(args.Order.RemainingQuantity == 0);
		}

		public void OrderTraded()
		{
			var sec = new Security() { Id = 1, Type = SecurityType.Future, Group = "GC", Product = "GC", Contract = "GCZ6" };
			var book = new OrderBook(sec);
			book.SetStatus(SecurityTradingStatus.Open);

			TradedEventArgs firedArgs = null;
			book.Traded += (o, e) => { firedArgs = e; };

			book.CreateLimitOrder(1, TimeInForce.Day, null, Side.Buy, 100, 2);
			book.CreateLimitOrder(3, TimeInForce.Day, null, Side.Sell, 100, 5);

			Debug.Assert(firedArgs != null);
			//Debug.Assert(firedArgs.Price == 100);
			//Debug.Assert(firedArgs.AggressorSide == Side.Sell);
			//Debug.Assert(firedArgs.Quantity == 5);
		}

		public void OrderMarketClosed()
		{
			
		}
	}
}
