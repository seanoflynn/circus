using System;

using Circus.Cme;

namespace Tests.Cme
{
	public class MarketDataChannelImporterTest
	{
		public MarketDataChannelImporterTest()
		{
			Test();
		}

		public void Test()
		{
			var mdci = MarketDataChannelImporter.Load("Cme/Resources/config.xml");
		}
	}
}
