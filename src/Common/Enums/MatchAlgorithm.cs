using System;

namespace Circus.Common
{
	public enum MatchAlgorithm
	{
		Fifo = 'F',
		Configurable = 'K',
		ProRata = 'C',
		Allocation = 'A',
		FifoWithLmm = 'T',
		ThresholdProRata = 'O',
		FifoWithTopAndLmm = 'S',
		ThresholdProRataWithLmm = 'Q',
		EurodollarOptions = 'Y',
	}	
}