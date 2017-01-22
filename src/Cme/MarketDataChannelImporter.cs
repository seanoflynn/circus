using System;
using System.Net;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

using Circus.Common;
using Circus.Fix;

namespace Circus.Cme
{	
	public class MarketDataChannelImporter
	{
		public static MarketDataChannel Load(string file, string product, bool overwriteWithLoopbackIP = false)
		{
			return Load(file, overwriteWithLoopbackIP).Find(x => x.Products.Contains(product));
		}

		public static List<MarketDataChannel> Load(string file, bool overwriteWithLoopbackIP = false)
		{
			var xml = XDocument.Load(file);

			var channels = new List<MarketDataChannel>();

			foreach (var channelNode in xml.Root.Descendants("channel"))
			{
				var channel = new MarketDataChannel()
				{
					Id = Convert.ToInt32(channelNode.Attribute("id").Value),
					Label = channelNode.Attribute("label").Value,
				};

				foreach (var productNode in channelNode.Descendants("product"))
				{
					var product = productNode.Attribute("code").Value;
					var grp = productNode.Elements().First().Attribute("code").Value;
					channel.Products.Add(product);
				}

				foreach (var connectionNode in channelNode.Descendants("connection"))
				{
					var conn = new MarketDataChannelConnection()
					{
						Id = connectionNode.Attribute("id").Value,
						Type = GetType(connectionNode.Descendants("type").First().Attribute("feed-type").Value),
						Protocol = GetProtocol(connectionNode.Descendants("protocol").First().Value),
						Port = Convert.ToInt32(connectionNode.Descendants("port").First().Value),
						Feed = connectionNode.Descendants("feed").First().Value
					};

					if (connectionNode.Descendants("ip").Any())
					{
						if (overwriteWithLoopbackIP)
						{
							conn.IPAddress = IPAddress.Loopback;
						}
						else
						{
							string address = connectionNode.Descendants("ip").First().Value;
							conn.IPAddress = IPAddress.Parse(address);
						}
					}

					foreach (var hostIPNode in connectionNode.Descendants("host-ip"))
					{
						if (overwriteWithLoopbackIP)
						{
							if (!conn.HostIPAddresses.Contains(IPAddress.Loopback))
								conn.HostIPAddresses.Add(IPAddress.Loopback);
						}
						else
						{
							string address = hostIPNode.Value;
							conn.HostIPAddresses.Add(IPAddress.Parse(address));
						}
					}

					channel.Connections.Add(conn);
				}

				channels.Add(channel);
			}

			return channels;
		}

		public static string ConvertToLoopback(string address)
		{
			return IPAddress.Loopback.ToString();
			//var parts = address.Split('.');
			//parts[0] = "127";
			//return String.Join(".", parts);
		}

		public static MarketDataChannelConnectionType GetType(string code)
		{
			switch (code)
			{
				case "S":
					return MarketDataChannelConnectionType.Snapshot;
				case "SMBO":
					return MarketDataChannelConnectionType.SnapshotMBO;
				case "I":
					return MarketDataChannelConnectionType.Incremental;
				case "N":
					return MarketDataChannelConnectionType.InstrumentReplay;				
				case "H":
					return MarketDataChannelConnectionType.HistoricalReplay;				
			}

			throw new NotSupportedException("unsupported market data channel connection type: " + code);
		}

		public static MarketDataChannelProtocol GetProtocol(string name)
		{
			switch (name)
			{
				case "UDP/IP":
					return MarketDataChannelProtocol.Udp;
				case "TCP/IP":
					return MarketDataChannelProtocol.Tcp;
			}

			throw new NotSupportedException("unsupported market data channel protocol: " + name);
		}
	}	
}