using System;

namespace Circus.Fix
{
	public abstract class ValueFieldAttribute : FieldAttribute
	{
		public Tag Tag { get; private set; }

		protected ValueFieldAttribute(int order, Tag tag) : base(order)
		{
			Tag = tag;
		}

		public abstract object Decode(byte[] data);
	}
}
