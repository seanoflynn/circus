using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Net;
using System.Net.Sockets;

using Circus.Common;

namespace Circus.Cme
{
	public class FixUdpClient
	{
		public event EventHandler<MdpHeartbeat> HeartbeatReceived;
		public event EventHandler<SecurityDefinition> SecurityDefinitionReceived;
		public event EventHandler<StatusUpdate> StatusUpdateReceived;
		public event EventHandler<SnapshotUpdate> SnapshotUpdateReceived;
		public event EventHandler<IncrementalUpdate> IncrementalUpdateReceived;

		private IPEndPoint ipEndPoint;

		private TimeSpan heartbeatInterval;
		private Timer timer;

		private DateTime lastMessageReceived = DateTime.MinValue;
		private DateTime lastSendingTimeReceived = DateTime.MinValue;
		private int nextSequenceNumberToReceive = 1;
		private int lastSequenceNumberProcessed;

		private bool isListening = true;

		public FixUdpClient(IPAddress address, int port)
		{
			ipEndPoint = new IPEndPoint(address, port);
		}

		public void Listen()
		{
			Console.WriteLine("listening on " + ipEndPoint);

			Task.Run(async () =>
			{
				using (var udpClient = new UdpClient(ipEndPoint))
				{					
					while (isListening)
					{
						//IPEndPoint object will allow us to read datagrams sent from any source.
						var receivedResults = await udpClient.ReceiveAsync();
						Process(receivedResults.Buffer);
					}
				}
			});
		}

		private void Process(byte[] data)
		{
			var message = MdpMessage.GetMessage(data);

			if (message == null)
			{
				// error
				return;
			}

			message.Validate();

			Console.WriteLine($"C <- {message.GetType().Name} {message}");
			lastMessageReceived = DateTime.UtcNow;

			if (!ValidateHeader(message))
				return;

			HandleMessage(message);
		}	

		private bool ValidateHeader(MdpMessage message)
		{
			//if (message.Header.SequenceNumber < nextSequenceNumberToReceive)
			//	return false;

			//if (message.Header.SequenceNumber > nextSequenceNumberToReceive)
			{
				// TODO: Handle skipped incoming message
				//return false;
			}

			nextSequenceNumberToReceive++;

			return true;
		}

		private void HandleMessage(MdpMessage message)
		{ 
			if (message is MdpHeartbeat && HeartbeatReceived != null)
				HeartbeatReceived(this, (MdpHeartbeat)message);
			else if (message is SecurityDefinition && SecurityDefinitionReceived != null)
				SecurityDefinitionReceived(this, (SecurityDefinition)message);
			else if (message is StatusUpdate && StatusUpdateReceived != null)
				StatusUpdateReceived(this, (StatusUpdate)message);			
			else if (message is SnapshotUpdate && SnapshotUpdateReceived != null)
				SnapshotUpdateReceived(this, (SnapshotUpdate)message);			
			else if( message is IncrementalUpdate && IncrementalUpdateReceived != null)
				IncrementalUpdateReceived(this, (IncrementalUpdate)message);

			//lastSequenceNumberProcessed = message.Header.SequenceNumber;
		}

		public void StartHeartbeat(int interval, bool sendTestRequest = false)
		{
			heartbeatInterval = TimeSpan.FromSeconds(interval);
			if( sendTestRequest)
				lastMessageReceived = DateTime.MinValue;
			timer = new Timer(CheckHeartbeat, null, 0, 1000);
		}

		public void StopHeartbeat()
		{
			if (timer != null)
				timer.Dispose();
		}

		private void CheckHeartbeat(Object stateInfo)
		{
			// send a test request if we need to
			if (DateTime.UtcNow < lastMessageReceived + heartbeatInterval + TimeSpan.FromSeconds(2))
				throw new Exception("no heartbeat in x seconds");			
		}
	}
}