using System;
using System.Collections.Generic;
using System.Threading;
using System.Net;
using System.Net.Sockets;

using Circus.Common;

namespace Circus.Cme
{
	public enum MarketDataChannelProtocol
	{
		Tcp,
		Udp,
	}

	public enum MarketDataChannelConnectionType
	{
		HistoricalReplay,
		Incremental,
		InstrumentReplay,
		Snapshot,
		SnapshotMBO,
	}

	public class MarketDataChannelConnection
	{
		public string Id { get; set; }
		public MarketDataChannelConnectionType Type { get; set; }
		public MarketDataChannelProtocol Protocol { get; set; }
		public IPAddress IPAddress { get; set; }
		public List<IPAddress> HostIPAddresses { get; set; } = new List<IPAddress>();
		public int Port { get; set; }
		public string Feed { get; set; }
	}


	public class MarketDataChannel
	{
		public int Id { get; set; }
		public string Label { get; set; }
		public List<MarketDataChannelConnection> Connections { get; set; } = new List<MarketDataChannelConnection>();
		public List<string> Products { get; set; } = new List<string>();
	}
}