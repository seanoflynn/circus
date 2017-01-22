using System;

using Circus.Common;
using Circus.Fix;

namespace Circus.Cme
{
	public class BusinessLevelReject : Message
	{
		[EnumField(1, Tag.RefMsgType, true)]
		public MessageType RefType { get; set; }

		[IntField(2, Tag.RefSeqNum, 0, int.MaxValue)]
		public int RefSequenceNumber { get; set; }

		[EnumField(3, Tag.BusinessRejectReason)]
		public BusinessLevelRejectReason Reason { get; set; }

		[StringField(4, Tag.Text)]
		public string Text { get; set; }

		[StringField(5, Tag.BusinessRejectRefID)]
		public string BusinessRejectReferenceId { get; set; }

		[BoolField(6, Tag.ManualOrderIndicator)]
		public bool IsManualOrder { get; set; }

		[EnumField(7, Tag.CustOrderHandlingInst, true)]
		public CustomerOrderHandlingInstruction? CustomerOrderHandlingInstruction { get; set; }

		public BusinessLevelReject() : base(MessageType.BusinessLevelReject)
		{ }

		public BusinessLevelReject(Message message, BusinessLevelRejectReason reason) 
			: base(MessageType.BusinessLevelReject)
		{
			RefType = message.Header.Type;
			RefSequenceNumber = message.Header.SequenceNumber;

			Reason = reason;
			Text = "lookup table";

			IsManualOrder = false;
		}
	}
}
