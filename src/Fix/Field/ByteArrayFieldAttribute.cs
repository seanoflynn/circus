using System;
using System.Collections.Generic;
using System.Text;

namespace Circus.Fix
{
	public class ByteArrayFieldAttribute : ValueFieldAttribute
	{
		public static List<Tag> Tags = new List<Tag>() { Tag.RawDataLength };

		public Tag DataTag { get; private set; }

		public ByteArrayFieldAttribute(int order, Tag lengthTag)
			: base(order, lengthTag)
		{
			//DataTag = dataTag;
		}

		public override byte[] Encode(object value)
		{
			return Encode(Tag, (byte[])value);
		}

		public override object Decode(byte[] data)
		{
			return data;
		}

		public override bool Validate(object value)
		{
			return true;
		}

		public override string ToString(object value)
		{
			return $"{Tag}({(int)Tag})={Encoding.UTF8.GetString((byte[])value)} ";
		}
	}
}