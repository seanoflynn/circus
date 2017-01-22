namespace Circus.Common
{
	public enum SessionLevelRejectReason
	{
		InvalidTagNumber = 0,
		RequiredTagMissing = 1,
		TagNotDefinedForMessageType = 2,
		UndefinedTag = 3,
		TagSpecifiedWithoutValue = 4,
		ValueIsIncorrect = 5,
		IncorrectDataFormat = 6,
		DecryptionProblem = 7,
		SignatureProblem = 8,
		CompIdProblem = 9,
		SendingTimeAccuracyProblem = 10,
		InvalidMsgType = 11,
		MassQuoteMessageViolation = 70,
		MassQuoteEntryViolation = 71,
		MessagingControlMessageViolation = 72,
		MessagingCancelMessageViolation = 73,
	}	
}
