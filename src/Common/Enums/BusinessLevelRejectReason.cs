namespace Circus.Common
{
	public enum BusinessLevelRejectReason
	{
		Other = 0,
		UnknownId = 1,
		UnknownSecurity = 2,
		UnsupportedMessageType = 3,
		ApplicationNotAvailable = 4,
		ConditionallyRequiredFieldMissing = 5,
		NotAuthorized = 6,
		DeliveryToFirmNotAvailable = 7,
	}
	
}
