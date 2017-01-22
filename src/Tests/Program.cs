using System;

namespace Tests
{
	public class Program
	{
		public static void Main()
		{
            // Common
            //new OrderTest();
            //new SecurityTest();
            		
            // Fix
            new Fix.FixTest();
            
			// Server
			new Server.OrderBookTest();
			new Server.TradingSessionTest();

			// Cme
			new Cme.FixTest();
			new Cme.MarketDataChannelImporterTest();
			new Cme.TradingSessionImporterTest();
			new Cme.SecurityDefinitionImporterTest();
			new Cme.TradingEngineTest();
		}
	}
}