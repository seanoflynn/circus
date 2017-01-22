using System;
using System.Net;
using System.Threading;

using Circus.Common;
using Circus.Cme;

namespace Examples
{	
	public class CmeMdpExample
	{
		public static void Run()
		{
			Security sec = new Security() { Id = 1, Type = SecurityType.Future, Group = "GC", Product = "GC", Contract = "GCZ6" };
			
			var mds = new FixUdpServer(new MarketDataChannelConnection() { IPAddress = IPAddress.Loopback, Port = 8453 });
			var mdc = new FixUdpClient(IPAddress.Loopback, 8453);
			mdc.Listen();
			mds.Send(new SecurityDefinition(sec));
			mds.Send(new StatusUpdate(StatusUpdateType.Group, sec, SecurityTradingStatus.Halt, StatusChangeReason.MarketEvent, SecurityTradingEvent.ImpliedMatchingOff));

            var volumeUpdate = new IncrementalUpdate(MatchEventIndicator.LastVolume, DateTime.UtcNow);
            volumeUpdate.MDEntries.Add(MarketDataUpdateDataBlock.VolumeNew(sec, 99, 12733));
            mds.Send(volumeUpdate);

            Thread.Sleep(1000);			
		}
	}
}