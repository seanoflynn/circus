using System;
using System.IO;
using System.Collections;
using System.Text;

namespace Circus.Fix
{
	public class GroupFieldAttribute : FieldAttribute
	{
		public Tag CountTag { get; private set; }
		public bool DisplayCountIfZero { get; private set; }

		public GroupFieldAttribute(int order, Tag countTag, bool displayCountIfZero = false)
			: base(order)
		{
			CountTag = countTag;
			DisplayCountIfZero = displayCountIfZero;
		}

		public override byte[] Encode(object value)
		{
			var list = (IList)value;

			if (!DisplayCountIfZero && list.Count < 1)
				return new byte[0];

			MemoryStream stream = new MemoryStream();

			var x = Encode(CountTag, Encoding.UTF8.GetBytes(list.Count.ToString()));

			stream.Write(x, 0, x.Length);

			foreach (var sec in list)
			{
				byte[] b = ((Section)sec).Encode();
				stream.Write(b, 0, b.Length);
			}

			return stream.ToArray();
		}

		public override bool Validate(object value) { return true; }

		public override string ToString(object value)
		{
			StringBuilder sb = new StringBuilder();
			sb.Append($"{CountTag}({(int)CountTag})={((ICollection)value).Count.ToString()} ");

			foreach (var i in ((ICollection)value))
				sb.Append(((Section)i).ToString());

			return sb.ToString();
		}
	}
}
