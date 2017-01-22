using System;
using System.Text;

namespace Circus.Fix
{
	public sealed class BitfieldFieldAttribute : ValueFieldAttribute
	{
		private const byte ZeroByte = 48;  // Y
		private const byte OneByte = 49; // N

		private const string ZeroString = "0";
		private const string OneString = "1";

		public int Length { get; private set;}

		private uint[] comp;

		public BitfieldFieldAttribute(int order, Tag tag, int length)
			: base(order, tag)
		{
			Length = length;
			comp = new uint[length];
			for (int i = 0; i < Length; i++)
				comp[i] = Convert.ToUInt32(Math.Pow(2, i));
		}

		public override byte[] Encode(object value)
		{
			byte[] val = new byte[Length];

			for (int i = 0; i < Length; i++)
			{
				val[i] = (((uint)value) & comp[i]) < 1 ? ZeroByte : OneByte;
			}

			return Encode(Tag, val);
		}

		public override object Decode(byte[] data)
		{
			if (data.Length != Length)
				throw new Exception("bitfield fix field is incorrect length");

			uint num = 0;

			for (int i = 0; i < Length; i++)
			{
				if (data[i] == OneByte)
					num += comp[i];
			}

			return num;
		}

		public override bool Validate(object value)
		{
			return true;
		}

		public override string ToString(object value)
		{
			StringBuilder val = new StringBuilder(Length);

			for (int i = 0; i < Length; i++)
			{
				val.Append((((uint)value) & comp[i]) < 1 ? ZeroString : OneString);
			}

			return $"{Tag}({(int)Tag})={val} ";
		}
	}
}