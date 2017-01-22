using System;
using System.Text;
using System.Threading;
using System.Linq;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

using Circus.Common;

namespace Circus.Cme
{
	public class FixTcpClient
	{
		public const int ReadBufferSize = 256;

		public event EventHandler<Logon> LogonReceived;
		public event EventHandler<Heartbeat> HeartbeatReceived;
		public event EventHandler<TestRequest> TestRequestReceived;
		public event EventHandler<ResendRequest> ResendRequestReceived;
		public event EventHandler<SequenceReset> SequenceResetReceived;
		public event EventHandler<BusinessLevelReject> BusinessLevelRejectReceived;
		public event EventHandler<SessionLevelReject> SessionLevelRejectReceived;
		public event EventHandler<Logout> LogoutReceived;

		public event EventHandler<NewOrder> NewOrderReceived;
		public event EventHandler<CancelReplaceRequest> CancelReplaceRequestReceived;
		public event EventHandler<CancelReject> CancelRejectReceived;
		public event EventHandler<CancelRequest> CancelRequestReceived;

		public event EventHandler<NewOrderAck> NewOrderAckReceived;
		public event EventHandler<CancelReplaceAck> CancelReplaceAckReceived;
		public event EventHandler<CancelAck> CancelAckReceived;
		public event EventHandler<Reject> RejectReceived;
		public event EventHandler<Fill> FillReceived;

		private static int nextId = 0;

		public string SenderCompanyId { get; set; }
		public string SenderTraderId { get; set; }
		public string SenderLocationId { get; set; }

		public string TargetCompanyId { get; set; }
		public string TargetTraderId { get; set; }
		public string TargetLocationId { get; set; }

		public int Id { get; private set; }
		private TcpClient tcpClient;

		private TimeSpan heartbeatInterval;
		private Timer timer;

		private DateTime lastMessageReceived = DateTime.MinValue;
		private DateTime lastSendingTimeReceived = DateTime.MinValue;
		private bool isWaitingToReceiveTestRequestReponse;
		private string testRequestId;
		private int nextSequenceNumberToSend = 1;

		private DateTime lastMessageSent;
		private int nextSequenceNumberToReceive = 1;
		private int lastSequenceNumberProcessed;

		public bool IsLoggedOn { get; set; }

		public FixTcpClient(string companyId, string traderId, string locationId, TcpClient client)
		{
			Id = nextId++;
			SenderCompanyId = companyId;
			SenderTraderId = traderId;
			SenderLocationId = locationId;

			tcpClient = client;
			tcpClient.NoDelay = true;
			tcpClient.ReceiveBufferSize = ReadBufferSize;

			HeartbeatReceived += HandleHeartbeat;
			TestRequestReceived += HandleTestRequest;
		}

		public FixTcpClient(string compId, string subId, string locationId, string targetCompanyId, 
		                 string targetTraderId, string targetLocationId)
			: this(compId, subId, locationId, new TcpClient())
		{
			TargetCompanyId = targetCompanyId;
			TargetTraderId = targetTraderId;
			TargetLocationId = targetLocationId;
		}

		public async void Connect(IPAddress address, int port)
		{
			await tcpClient.ConnectAsync(address, port);

			Listen();
		}

		public void Disconnect()
		{
			tcpClient.Client.Shutdown(SocketShutdown.Both);
		}

		public void Send(Message message)
		{
			if (!tcpClient.Connected)
				return;

			message.Header.SenderCompanyId = SenderCompanyId;
			message.Header.SenderTraderId = SenderTraderId;
			message.Header.SenderLocationId = SenderLocationId;

			message.Header.TargetCompanyId = TargetCompanyId;
			message.Header.TargetTraderId = TargetTraderId;
			message.Header.TargetLocationId = TargetLocationId;

			message.Header.SendTime = DateTime.UtcNow;

			message.Header.SequenceNumber = nextSequenceNumberToSend;
			message.Header.LastSequenceNumberProcessed = lastSequenceNumberProcessed;
			nextSequenceNumberToSend++;

			message.Validate();
			byte[] data = message.Encode();

			Console.WriteLine($"{Id} -> {message.GetType().Name} {message}");
			//Console.WriteLine(message.ToRawString());

			// TODO: cache messages in case we have to do a resend request
			// prehaps create two queues, one normal that is always being processed
			// and another to populate if we skip a packet

			tcpClient.GetStream().Write(data, 0, data.Length);

			lastMessageSent = DateTime.UtcNow;
		}

		public async void Listen()
		{
			var buffer = new byte[ReadBufferSize];
			var bytes = new List<byte>();
			int messageLength = 0;

			while (tcpClient.Connected)
			{
				var newByteCount = await tcpClient.GetStream().ReadAsync(buffer, 0, buffer.Length);

				if (newByteCount < 1)
					break;

				bytes.AddRange(buffer.Take(newByteCount));

				if (messageLength == 0)
				{					
					if (bytes.Count(x => x == (byte)1) < 2)
						continue;
					
					messageLength = Message.GetLength(bytes.ToArray());
				}

				if (bytes.Count >= messageLength )
				{
					var data = bytes.Take(messageLength ).ToArray();
					bytes.RemoveRange(0, messageLength );
					messageLength = 0;

					ValidateChecksum(data);

					var message = Message.GetMessage(data);

					if (message == null)
					{
						Send(new SessionLevelReject(SessionLevelRejectReason.InvalidMsgType, ""));
						return;
					}

					message.Validate();

					Console.WriteLine($"{Id} <- {message.GetType().Name} {message}");
					lastMessageReceived = DateTime.UtcNow;

					if (!ValidateHeader(message))
						return;
					
					HandleMessage(message);
				}
			}

			Console.WriteLine($"{Id}: connection closed");
		}

		private bool ValidateChecksum(byte[] data)
		{
			int cs1 = data.Take(data.Length-7).Sum(x=>x) % 256;
			int cs2 = Convert.ToInt32(Encoding.UTF8.GetString(data, data.Length - 4, 3));

			if (cs1 != cs2)
			{
				Send(new SessionLevelReject(SessionLevelRejectReason.ValueIsIncorrect, "Checksum (10) tag has an incorrect value"));
				Disconnect();
				return false;
			}

			return true;
		}

		private bool ValidateHeader(Message message)
		{
			if (message.Header.SequenceNumber < nextSequenceNumberToReceive)
				return false;

			if (message.Header.SequenceNumber > nextSequenceNumberToReceive)
			{
				// TODO: Handle skipped incoming message
				return false;
			}

			nextSequenceNumberToReceive++;

			if (TargetCompanyId != null && message.Header.SenderCompanyId != TargetCompanyId)
			{
				Send(new SessionLevelReject(SessionLevelRejectReason.ValueIsIncorrect, "Invalid SenderCompID (49) tag"));
				return false;
			}

			if (TargetTraderId != null && message.Header.SenderTraderId != TargetTraderId)
			{
				Send(new SessionLevelReject(SessionLevelRejectReason.ValueIsIncorrect, "Invalid SenderSubID (50) tag"));
				return false;
			}

			if (TargetLocationId != null && message.Header.SenderLocationId != TargetLocationId)
			{
				Send(new SessionLevelReject(SessionLevelRejectReason.ValueIsIncorrect, "Invalid SenderLocationID (142) tag"));
				return false;
			}

			if (message.Header.TargetCompanyId != SenderCompanyId)
			{
				Send(new SessionLevelReject(SessionLevelRejectReason.ValueIsIncorrect, "Invalid TargetCompID (56) tag, should be: " + SenderCompanyId));
				return false;
			}

			if (message.Header.TargetTraderId != SenderTraderId)
			{
				Send(new SessionLevelReject(SessionLevelRejectReason.ValueIsIncorrect, "Invalid TargetSubID (57) tag, should be: " + SenderTraderId));
				return false;
			}

			if (message.Header.TargetLocationId != SenderLocationId)
			{
				Send(new SessionLevelReject(SessionLevelRejectReason.ValueIsIncorrect, "Invalid TargetLocationID (143) tag, should be: " + SenderLocationId));
				return false;
			}

			return true;
		}

		private void HandleMessage(Message message)
		{ 
			if (message is Logon && LogonReceived != null)
				LogonReceived(this, (Logon)message);
			else if (message is Heartbeat && HeartbeatReceived != null)
				HeartbeatReceived(this, (Heartbeat)message);			
			else if (message is TestRequest && TestRequestReceived != null)
				TestRequestReceived(this, (TestRequest)message);			
            else if( message is SequenceReset && SequenceResetReceived != null)
				SequenceResetReceived(this, (SequenceReset)message);
            else if( message is ResendRequest && ResendRequestReceived != null)
                ResendRequestReceived(this, (ResendRequest)message);
            else if( message is SessionLevelReject && SessionLevelRejectReceived != null)                    
                SessionLevelRejectReceived(this, (SessionLevelReject)message);
            else if( message is BusinessLevelReject && BusinessLevelRejectReceived != null)                    
                BusinessLevelRejectReceived(this, (BusinessLevelReject)message);
            else if( message is Logout && LogoutReceived != null)
                LogoutReceived(this, (Logout)message);

			else if (message is NewOrder && NewOrderReceived != null)
				NewOrderReceived(this, (NewOrder)message);
			else if (message is CancelRequest && CancelRequestReceived != null)
				CancelRequestReceived(this, (CancelRequest)message);
			else if (message is CancelReplaceRequest && CancelReplaceRequestReceived != null)
				CancelReplaceRequestReceived(this, (CancelReplaceRequest)message);
			else if (message is CancelReject && CancelRejectReceived != null)
				CancelRejectReceived(this, (CancelReject)message);
			
			else if (message is NewOrderAck && NewOrderAckReceived != null)
				NewOrderAckReceived(this, (NewOrderAck)message);
			else if (message is CancelReplaceAck && CancelReplaceAckReceived != null)
				CancelReplaceAckReceived(this, (CancelReplaceAck)message);
			else if (message is CancelAck && CancelAckReceived != null)
				CancelAckReceived(this, (CancelAck)message);
			else if (message is Reject && RejectReceived != null)
				RejectReceived(this, (Reject)message);			
			else if (message is Fill && FillReceived != null)
				FillReceived(this, (Fill)message);

			lastSequenceNumberProcessed = message.Header.SequenceNumber;
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
			if (DateTime.UtcNow > lastMessageSent + heartbeatInterval)
				Send(new Heartbeat());			

			// send a test request if we need to
			if (DateTime.UtcNow < lastMessageReceived + heartbeatInterval + TimeSpan.FromSeconds(2))
				return;

			if (isWaitingToReceiveTestRequestReponse &&
				DateTime.UtcNow < lastMessageReceived + heartbeatInterval + heartbeatInterval)
				throw new Exception("no response to test request");

			if (!isWaitingToReceiveTestRequestReponse)
			{				
				testRequestId = (new Random()).Next(1,999).ToString("D3");
				Send(new TestRequest(testRequestId));
				isWaitingToReceiveTestRequestReponse = true;
			}
		}

		private void HandleHeartbeat(object sender, Heartbeat heartbeat)
		{
			if (isWaitingToReceiveTestRequestReponse)
			{
				if (testRequestId == heartbeat.TestRequestId)
					isWaitingToReceiveTestRequestReponse = false;
			}
		}

		private void HandleTestRequest(object sender, TestRequest testRequest)
		{
			Send(new Heartbeat(testRequest.TestRequestId));
		}
	}
}