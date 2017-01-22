using System;
using System.Text;
using System.IO;

namespace Circus.Fix
{
	[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
	public abstract class FieldAttribute : Attribute
	{
		public int Order { get; private set; }

		protected FieldAttribute(int order)
		{
			Order = order;
		}

		public abstract byte[] Encode(object value);
		public abstract bool Validate(object value);
		public abstract string ToString(object value);

		public static byte[] Encode(Tag tag, byte[] value)
		{
			byte[] tagBytes = Encoding.UTF8.GetBytes(((int)tag).ToString());

			MemoryStream stream = new MemoryStream();
			stream.Write(tagBytes, 0, tagBytes.Length);
			stream.WriteByte((byte)'=');
			stream.Write(value, 0, value.Length);
			stream.WriteByte(1);
			return stream.ToArray();
		}

		public static byte[] Encode(Tag tag, byte value)
		{
			return Encode(tag, new byte[] { value });
		}
	}
}
