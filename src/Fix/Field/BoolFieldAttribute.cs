using System;

namespace Circus.Fix
{
	public class BoolFieldAttribute : ValueFieldAttribute
	{
		private const byte TrueByte = 89;  // Y
		private const byte FalseByte = 78; // N

		private const string TrueString = "Y";
		private const string FalseString = "N";

		public BoolFieldAttribute(int order, Tag tag)
			: base(order, tag)
		{ }

		public override byte[] Encode(object value)
		{
			return Encode(Tag, (bool)value ? TrueByte : FalseByte);
		}

		public override object Decode(byte[] data)
		{
			if (data.Length != 1)
				throw new Exception("bool field can only be one byte");
			
			return data[0] == TrueByte;
		}

		public override bool Validate(object value)
		{
			return true;
		}

		public override string ToString(object value)
		{
			return $"{Tag}({(int)Tag})={((bool)value ? TrueString : FalseString)} ";
		}
	}
}
