using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;

using Circus.Common;
using Circus.Fix;

namespace Circus.Cme
{
	public abstract class MdpMessage : Section
	{
		[EnumField(-100, Tag.MsgType, true)]
		public MessageType Type { get; set; }

		public MdpMessage(MessageType type)
		{
			Type = type;
		}

		private static MessageType GetType(byte[] data)
		{
			int l = data[3];

			return (MessageType)l;
		}

		public static MdpMessage GetMessage(byte[] data)
		{
			MessageType type = GetType(data);

			MdpMessage mes = null;

			if (type == MessageType.SecurityDefinition)
				mes = new SecurityDefinition();
			else if (type == MessageType.SecurityStatus)
				mes = new StatusUpdate();
			else if (type == MessageType.MarketDataSnapshotFullRefresh)
				mes = new SnapshotUpdate();
			else if (type == MessageType.MarketDataIncrementalRefresh)
				mes = new IncrementalUpdate();

			mes.Decode(data);

			return mes;
		}
	}
}
