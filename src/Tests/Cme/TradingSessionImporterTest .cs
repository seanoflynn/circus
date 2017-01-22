using System;
using Circus.Cme;

namespace Tests.Cme
{
	public class TradingSessionImporterTest
	{
		public TradingSessionImporterTest()
		{
			Test();
		}

		public void Test()
		{
			var tsi = TradingSessionImporter.Load("Cme/Resources/TradingSessionList.dat");
		}
	}
}
