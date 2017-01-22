using System;

using Circus.Common;
using Circus.Fix;

namespace Circus.Cme
{
	public class Header : Section
	{
		[StringField(1, Tag.BeginString)]
		public string FixVersion { get; set; } = "FIX.4.2";
		[IntField(2, Tag.BodyLength, 0, 9999999)]
		public int BodyLength { get; set; }
		[EnumField(3, Tag.MsgType, true)]
		public MessageType Type { get; set; }

		[IntField(4, Tag.MsgSeqNum)]
		public int SequenceNumber { get; set; }
		[IntField(5, Tag.LastMsgSeqNumProcessed)]
		public int LastSequenceNumberProcessed { get; set; }

		[StringField(6, Tag.SenderCompID)]
		public string SenderCompanyId { get; set; }
		[StringField(7, Tag.SenderSubID)]
		public string SenderTraderId { get; set; }
		[StringField(8, Tag.SenderLocationID)]
		public string SenderLocationId { get; set; }

		[StringField(9, Tag.TargetCompID)]
		public string TargetCompanyId { get; set; }
		[StringField(10, Tag.TargetSubID)]
		public string TargetTraderId { get; set; }
		[StringField(11, Tag.TargetLocationID)]
		public string TargetLocationId { get; set; }

		[DateTimeField(12, Tag.SendingTime, DateTimeFormat.UtcTimestamp)]
		public DateTime SendTime { get; set; }
		[DateTimeField(13, Tag.OrigSendingTime, DateTimeFormat.UtcTimestamp)]
		public DateTime? OriginalSendTime { get; set; }

		[BoolField(14, Tag.PossDupFlag)]
		public bool? IsPossibleDuplicate { get; set; }
		[BoolField(15, Tag.PossResend)]
		public bool? IsPossibleResend { get; set; }
	}
}
