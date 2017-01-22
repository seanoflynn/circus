using System;
using System.Text;

namespace Circus.Fix
{
	public sealed class EnumFieldAttribute : ValueFieldAttribute
	{
		public bool IsChar { get; private set; }

		public EnumFieldAttribute(int order, Tag tag, bool isChar = false)
			: base(order, tag)
		{
			IsChar = isChar;
		}

		public override byte[] Encode(object value)
		{
			if (IsChar)
				return Encode(Tag, Convert.ToByte((int)value));
			else
				return Encode(Tag, Encoding.UTF8.GetBytes(((int)value).ToString()));
		}

		public override object Decode(byte[] data)
		{
			if (IsChar)
				return Convert.ToInt32(data[0]);
			else
				return Convert.ToInt32(Encoding.UTF8.GetString(data));
		}

		public override bool Validate(object value)
		{
			return true;
		}

		public override string ToString(object value)
		{
			if (IsChar)
			{
				var c = Encoding.UTF8.GetString(new byte[] { Convert.ToByte(value) });
				return $"{Tag}({(int)Tag})={value.ToString()}({c}) ";
			}
			else
				return $"{Tag}({(int)Tag})={value.ToString()}({(int)value}) ";
		}
	}
}