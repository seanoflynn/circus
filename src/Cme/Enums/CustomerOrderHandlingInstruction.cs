namespace Circus.Cme
{
	public enum CustomerOrderHandlingInstruction
	{
		PhoneSimple = 'A',
		PhoneComplex = 'B',
		FcmProvidedScreen = 'C',
		OtherProvidedScreen = 'D',
		ClientProvidedPlatformControlledByFcm = 'E',
		ClientProvidedPlatformDirectToExchange = 'F',
		FcmApiOrFix = 'G',
		AlgoEngine = 'H',
		PriceAtExecution = 'J',
		DeskElectronic = 'W',
		DeskPit = 'X',
		ClientElectronic = 'Y',
		ClientPit = 'Z',
	}	

	
}
