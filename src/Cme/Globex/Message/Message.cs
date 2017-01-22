using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;

using Circus.Common;
using Circus.Fix;

namespace Circus.Cme
{
	public abstract class Message : Section
	{
		[SubsectionField(int.MinValue)]
		public Header Header { get; } = new Header();

		[SubsectionField(int.MaxValue)]
		public Trailer Trailer { get; } = new Trailer();

		public Message(MessageType type)
		{
			Header.Type = type;
		}

		public new byte[] Encode()
		{
			// set BodyLength and CheckSum
			var head = Header.EncodeExcept(new Tag[] { Tag.BeginString, Tag.BodyLength });
			var body = EncodeExceptSections();
			Header.BodyLength = head.Length + body.Length;
			var start = Header.EncodeOnly(new Tag[] { Tag.BeginString, Tag.BodyLength });
			Trailer.CheckSum = CalculateChecksum(start, head, body).ToString("D3");
			var trail = Trailer.Encode();

			List<byte> data = new List<byte>();

			data.AddRange(start);
			data.AddRange(head);
			data.AddRange(body);
			data.AddRange(trail);

			return data.ToArray();
		}

		private int CalculateChecksum(byte[] a, byte[] b, byte[] c)
		{
			return (a.Sum(x => x) + b.Sum(x => x) + c.Sum(x => x)) % 256;
		}

		public static int GetLength(byte[] data)
		{
			if (data.Count(x => x == (byte)1) < 2)
				return 0;

			int f = Array.IndexOf(data, (byte)1, 0);
			int s = Array.IndexOf(data, (byte)'=', f + 1);
			int e = Array.IndexOf(data, (byte)1, f + 1);

			string str = Encoding.UTF8.GetString(data, s + 1, e - s - 1);
			int l = Convert.ToInt32(str);

			return e + l + 8;
		}

		private static MessageType GetType(byte[] data)
		{
			int f = Array.IndexOf(data, (byte)1, 0);
			int f1 = Array.IndexOf(data, (byte)1, f + 1);

			int l = data[f1 + 4];

			return (MessageType)l;
		}

		private static OrderStatus GetOrderStatus(byte[] data)
		{
			byte a = (byte)1;
			byte b = (byte)'3';
			byte c = (byte)'9';
			byte d = (byte)'=';

			for (int i = 1; i < data.Length - 5; i++)
			{
				if (data[i] == a &&
					data[i + 1] == b &&
					data[i + 2] == c &&
					data[i + 3] == d)
				{
					return (OrderStatus)data[i + 4];
				}
			}

			return OrderStatus.Undefined;
		}

		public static Message GetMessage(byte[] data)
		{
			MessageType type = GetType(data);

			Message mes = null;

			if (type == MessageType.Logon)
				mes = new Logon();
			else if (type == MessageType.Heartbeat)
				mes = new Heartbeat();
			else if (type == MessageType.TestRequest)
				mes = new TestRequest();
			else if (type == MessageType.SequenceReset)
				mes = new SequenceReset();
			else if (type == MessageType.ResendRequest)
				mes = new ResendRequest();
			else if (type == MessageType.SessionLevelReject)
				mes = new SessionLevelReject();
			else if (type == MessageType.BusinessLevelReject)
				mes = new BusinessLevelReject();
			else if (type == MessageType.Logout)
				mes = new Logout();
			else if (type == MessageType.OrderSingle)
				mes = new NewOrder();
			else if (type == MessageType.OrderCancelReplaceRequest)
				mes = new CancelReplaceRequest();
			else if (type == MessageType.OrderCancelRequest)
				mes = new CancelRequest();
			else if (type == MessageType.OrderCancelReject)
				mes = new CancelReject();
			else if (type == MessageType.ExecutionReport)
			{
				var os = GetOrderStatus(data);

				if (os == OrderStatus.New)
					mes = new NewOrderAck();
				else if (os == OrderStatus.Replaced)
					mes = new CancelReplaceAck();
				else if (os == OrderStatus.Cancelled)
					mes = new CancelAck();
				else if (os == OrderStatus.Rejected)
					mes = new Reject();
				else if (os == OrderStatus.PartiallyFilled)
					mes = new Fill(true);
				else if (os == OrderStatus.Filled)
					mes = new Fill(false);
				else if (os == OrderStatus.Expired)
					mes = new Expire();
			}

			mes.Decode(data);

			return mes;
		}
	}
}
