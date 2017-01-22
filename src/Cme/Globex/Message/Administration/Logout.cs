using System;

using Circus.Common;
using Circus.Fix;

namespace Circus.Cme
{
	public class Logout : Message
	{
		[StringField(1, Tag.Text)]
		public string Text { get; set; }

		[IntField(2, Tag.NextExpectedMsgSeqNum, 0, int.MaxValue)]
		public int? NextExpectedSequenceNumber { get; set; }

		public Logout() : base(MessageType.Logout) { }
	}
}
