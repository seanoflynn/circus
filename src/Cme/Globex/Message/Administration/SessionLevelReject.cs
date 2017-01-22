using System;

using Circus.Common;
using Circus.Fix;

namespace Circus.Cme
{
	public class SessionLevelReject : Message
	{
		[IntField(1, Tag.RefSeqNum, 0, int.MaxValue)]
		public int RefSequenceNumber { get; set; }

		[IntField(2, Tag.RefTagID)]
		public int? RefTagId { get; set; }

		[EnumField(3, Tag.SessionRejectReason)]
		public SessionLevelRejectReason Reason { get; set; }

		[StringField(4, Tag.Text)]
		public string Text { get; set; }

		[BoolField(5, Tag.ManualOrderIndicator)]
		public bool IsManualOrder { get; set; }

		[EnumField(6, Tag.CustOrderHandlingInst, true)]
		public CustomerOrderHandlingInstruction? CustomerOrderHandlingInstruction { get; set; }

		public SessionLevelReject() : base(MessageType.SessionLevelReject)
		{ }

		public SessionLevelReject(SessionLevelRejectReason reason, string text) : base(MessageType.SessionLevelReject)
		{
			Reason = reason;
			Text = text;

			IsManualOrder = false;
		}
	}
}
