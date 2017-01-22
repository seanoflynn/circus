using System;
using System.Net;
using System.Threading;

using Circus.Common;
using Circus.Server;
using Circus.Cme;

namespace Examples
{	
	public class CmeExample
	{
		public static void Run()
		{
			Security sec = new Security() { Id = 1, Type = SecurityType.Future, Group = "GC", Product = "GC", Contract = "GCZ6" };			

			var tod = DateTime.UtcNow.TimeOfDay;

			var ts = new TradingSession();

			Random rand = new Random();
			int port = 7000 + rand.Next(0, 100);

            TradingEngine exc = new TradingEngine(null, ts, "CME", "G");
			exc.AddSecurity(sec);
			exc.Start(IPAddress.Loopback, port);

			ts.Update(SecurityTradingStatus.Open);

			Thread.Sleep(100);

			var client = new Client("ABC123N", "Operator1", "IE", "CME", "G", null, "Acc1");
			client.Connect(IPAddress.Loopback, port);

			Thread.Sleep(100);
			client.Logon("password");

			int oid1 = client.CreateLimitOrder(sec, Side.Buy, 3, 100);
            Thread.Sleep(100); // we need to wait to hear the ack to get an OrderId
			client.UpdateOrder(oid1, sec, OrderType.Limit, TimeInForce.Day, null, Side.Buy, 105, null, 5, null, null);
            client.DeleteOrder(oid1, sec, Side.Buy);

            Thread.Sleep(100);
		}
	}
}