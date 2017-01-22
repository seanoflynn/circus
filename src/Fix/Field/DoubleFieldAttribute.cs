using System;
using System.Text;

namespace Circus.Fix
{
	public class DoubleFieldAttribute : ValueFieldAttribute
	{
		public int Min { get; private set; }
		public int Max { get; private set; }

		public DoubleFieldAttribute(int order, Tag tag, int min = int.MinValue, int max = int.MaxValue)
			: base(order, tag)
		{
			Min = min;
			Max = max;
		}

		public override byte[] Encode(object value)
		{
			return Encode(Tag, Encoding.UTF8.GetBytes(((double)value).ToString("0.0000000########")));
		}

		public override object Decode(byte[] data)
		{
			return Convert.ToDouble(Encoding.UTF8.GetString(data));
		}

		public override bool Validate(object value)
		{
			if ((double)value < Min)
			{
				Console.WriteLine("value must be > min");
				return false;
			}

			if ((double)value > Max)
			{
				Console.WriteLine("value must be < max");
				return false;
			}

			return true;
		}

		public override string ToString(object value)
		{
			return $"{Tag}({(int)Tag})={((double)value).ToString()} ";
		}
	}
}
