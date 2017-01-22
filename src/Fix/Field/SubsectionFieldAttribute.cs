using System;

namespace Circus.Fix
{
	public class SubsectionFieldAttribute : FieldAttribute
	{
		public SubsectionFieldAttribute(int order) : base(order)
		{ }

		public override byte[] Encode(object value)
		{
			return ((Section)value).Encode();
		}

		public override bool Validate(object value)
		{
			return true;
		}

		public override string ToString(object value)
		{
			return ((Section)value).ToString();
		}
	}
}
