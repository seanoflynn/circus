using System;

using Circus.Common;
using Circus.Fix;

namespace Circus.Cme
{
	public class Heartbeat : Message
	{
		[StringField(1, Tag.TestReqID)]
		public string TestRequestId { get; set; }

		public Heartbeat() : base(MessageType.Heartbeat)
		{ }

		public Heartbeat(string testRequestId) : base(MessageType.Heartbeat) 
		{
			TestRequestId = testRequestId;
		}
	}
}
