using System;
using System.Collections.Generic;
using System.Threading;
using System.Net;
using System.Linq;
using System.Diagnostics;

using Circus.Common;
using Circus.Server;
using Circus.Cme;

namespace Tests.Cme
{
	public class TradingEngineTest
	{
		public TradingEngineTest()
		{
			TradingEngine();
		}

		public void TradingEngine()
		{
			var contract = "GCG7";
			var sec = SecurityDefinitionImporter.Load("Cme/Resources/secdef.dat", contract);
			var ts = TradingSessionImporter.Load("Cme/Resources/TradingSessionList.dat", sec.Group);
			var channels = MarketDataChannelImporter.Load("Cme/Resources/config.xml", sec.Product, true);

			int port = 7000 + (new Random()).Next(0, 100);

			var te = new TradingEngine(channels, ts, "CME", "G");
			te.AddSecurity(sec);
			te.Start(IPAddress.Loopback, port);

			ts.Update(SecurityTradingStatus.Open);

			var ch = channels.Connections.Find(x => x.Type == Circus.Cme.MarketDataChannelConnectionType.Incremental && x.Feed == "A");
			var dataClient = new FixUdpClient(ch.IPAddress, ch.Port);
			dataClient.IncrementalUpdateReceived += (sender, e) =>
			{
				Console.WriteLine("*** Incremental Update: " + e);
				foreach (var u in e.MDEntries)
				{
					Console.WriteLine("*** - " + u);
				}
			};
			dataClient.Listen();

			Thread.Sleep(100);

			var client = new Client("ABC123N", "Operator1", "IE", "CME", "G", null, "Acc1");
			client.Connect(IPAddress.Loopback, port);
			client.Logon("mmmbop");
			Thread.Sleep(1000);
			client.CreateLimitOrder(sec, Side.Buy, 2, 105);
			Thread.Sleep(1000);
			client.CreateLimitOrder(sec, Side.Buy, 2, 104);
			Thread.Sleep(1000);
			client.CreateLimitOrder(sec, Side.Sell, 5, 104);

			Thread.Sleep(10000);

			//while (true)
			//	Thread.Sleep(1000);
		}
	}
}