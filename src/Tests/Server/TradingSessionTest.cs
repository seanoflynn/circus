using System;
using System.Diagnostics;

namespace Tests.Server
{
	public class TradingSessionTest
	{
		public TradingSessionTest()
		{
			Creation();
		}

		public void Creation()
		{
			var tod = DateTime.UtcNow.TimeOfDay;

			var ot = tod + TimeSpan.FromSeconds(15);
			var ct = ot - TimeSpan.FromSeconds(60);
			//var ts = new TradingSession(ot, ct);
			//ts.Changed += (sender, e) => Console.WriteLine("state=" + e);
		}
	}
}
