using System;

using Circus.Common;
using Circus.Fix;

namespace Circus.Cme
{
	public class TestRequest : Message
	{
		[StringField(1, Tag.TestReqID)]
		public string TestRequestId { get; set; }

		public TestRequest() : base(MessageType.TestRequest)
		{ }

		public TestRequest(string testRequestId = null) : base(MessageType.TestRequest)
		{
			TestRequestId = testRequestId;
		}
	}
}
