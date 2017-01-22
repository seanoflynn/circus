using System;
using System.Text;

namespace Circus.Fix
{
	public class StringFieldAttribute : ValueFieldAttribute
	{
		public StringFieldAttribute(int order, Tag tag)
			: base(order, tag)
		{ }

		public override byte[] Encode(object value)
		{
			return Encode(Tag, Encoding.UTF8.GetBytes((string)value));
		}

		public override object Decode(byte[] data)
		{
			return Encoding.UTF8.GetString(data);
		}

		public override bool Validate(object value)
		{
			return true;
		}

		public override string ToString(object value)
		{
			return $"{Tag}({(int)Tag})={((string)value)} ";
		}
	}
}