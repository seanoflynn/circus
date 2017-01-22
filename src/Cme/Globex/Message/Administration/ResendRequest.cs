using System;

using Circus.Common;
using Circus.Fix;

namespace Circus.Cme
{
	public class ResendRequest : Message
	{
		[IntField(1, Tag.BeginSeqNo, 0, int.MaxValue)]
		public int BeginSequenceNumber { get; set; }

		[IntField(2, Tag.EndSeqNo, 0, int.MaxValue)]
		public int EndSequenceNumber { get; set; }

		public ResendRequest() : base(MessageType.ResendRequest) { }

		public ResendRequest(int begin, int end) : base(MessageType.ResendRequest) 
		{
			BeginSequenceNumber = begin;
			EndSequenceNumber = end;
		}
	}
}
