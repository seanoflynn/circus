using System;
using System.Net;
using System.Threading;

using Circus.Common;
using Circus.Server;

namespace Examples
{	
	public class OrderBookExample
	{
		public static void Run()
		{
			Security sec = new Security() { Id = 1, Type = SecurityType.Future, Group = "GC", Product = "GC", Contract = "GCZ6" };
						
            OrderBook ob = new OrderBook(sec);
            ob.Traded += (o, e) => { Console.WriteLine("traded qty=" + e.Fills[0].Quantity); };
            ob.SetStatus(SecurityTradingStatus.Open);

            ob.CreateLimitOrder(0, TimeInForce.Day, null, Side.Buy, 100, 3);
            ob.CreateLimitOrder(1, TimeInForce.Day, null, Side.Sell, 100, 5);            
            
			Thread.Sleep(100);
		}
	}
}