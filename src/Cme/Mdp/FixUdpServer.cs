using System;
using System.Threading;
using System.Net;
using System.Net.Sockets;

using Circus.Common;

namespace Circus.Cme
{
	public class FixUdpServer
	{
		//public event EventHandler<Trade> TradeReceived;
		//public event EventHandler<BookUpdate> BookUpdateReceived;

		private IPEndPoint ipEndPoint;
		private UdpClient udpClient;

		private TimeSpan heartbeatInterval;
		private Timer timer;

		private int nextSequenceNumberToSend = 1;
		private DateTime lastMessageSent;

		public FixUdpServer(MarketDataChannelConnection connection)
		{
			ipEndPoint = new IPEndPoint(connection.IPAddress, connection.Port);
			udpClient = new UdpClient();
			Console.WriteLine($"{connection.Type} market data udp server ready at {ipEndPoint}");
		}

		public async void Send(MdpMessage message)
		{			
			//message.Header.SequenceNumber = nextSequenceNumberToSend;
			nextSequenceNumberToSend++;

			message.Validate();
			byte[] data = message.Encode();

			Console.WriteLine($"S -> {message.GetType().Name} {message}");
			Console.WriteLine(message.ToRawString());

			await udpClient.SendAsync(data, data.Length, ipEndPoint);

			lastMessageSent = DateTime.UtcNow;
		}

		public void StartHeartbeat(int interval, bool sendTestRequest = false)
		{
			heartbeatInterval = TimeSpan.FromSeconds(interval);
			timer = new Timer(CheckHeartbeat, null, 0, 1000);
		}

		public void StopHeartbeat()
		{
			if (timer != null)
				timer.Dispose();
		}

		private void CheckHeartbeat(Object stateInfo)
		{
			if (DateTime.UtcNow > lastMessageSent + heartbeatInterval)
				Send(new MdpHeartbeat());
		}
	}
}