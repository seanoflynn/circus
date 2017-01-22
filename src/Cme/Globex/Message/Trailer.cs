using System;

using Circus.Common;
using Circus.Fix;

namespace Circus.Cme
{
	public sealed class Trailer : Section
	{
		[StringField(1, Tag.CheckSum)]
		public string CheckSum { get; set; }
	}
}
