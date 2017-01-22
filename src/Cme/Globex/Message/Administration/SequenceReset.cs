using System;

using Circus.Common;
using Circus.Fix;

namespace Circus.Cme
{
	public class SequenceReset : Message
	{
		[IntField(1, Tag.NewSeqNo, 0, int.MaxValue)]
		public int NewSequenceNumber { get; set; }

		[BoolField(2, Tag.GapFillFlag)]
		public bool? FillGap { get; set; }

		public SequenceReset() : base(MessageType.SequenceReset) { }

		public SequenceReset(int newSeqNum, bool? fillGap) : base(MessageType.SequenceReset) 
		{
			NewSequenceNumber = newSeqNum;
			FillGap = fillGap;
		}
	}
}
