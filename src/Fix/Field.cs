using System;

namespace Circus.Fix
{
	public class Field
	{
		public Tag Tag { get; set; }
		public byte[] Data { get; set; }

		public Field(Tag tag, byte[] data)
		{
			Tag = tag;
			Data = data;
		}
	}
}