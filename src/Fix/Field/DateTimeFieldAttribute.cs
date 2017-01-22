using System;
using System.Linq;
using System.Globalization;
using System.Text;

namespace Circus.Fix
{
	public enum DateTimeFormat
	{
		UtcTimestamp,
		UtcTimeOnly,
		LocalMktDate,
		UtcDate,
		MonthYear
	}

	public class DateTimeFieldAttribute : ValueFieldAttribute
	{
		private const string UtcTimestampFormat = "yyyyMMdd-HH:mm:ss.fff";
		private const string UtcTimeOnlyFormat = "HH:mm:ss.fff";
		private const string LocalMktDateFormat = "yyyyMMdd";
		private const string UtcDateFormat = "yyyyMMdd";
		private const string MonthYear = "yyyyMM";

		public DateTimeFormat Format { get; private set; }
		private string form;

		public DateTimeFieldAttribute(int order, Tag tag, DateTimeFormat format = DateTimeFormat.UtcTimestamp)
			: base(order, tag)
		{
			Format = format;
			if (Format == DateTimeFormat.UtcTimestamp)
				form = UtcTimestampFormat;
			else if (Format == DateTimeFormat.UtcTimeOnly)
				form = UtcTimeOnlyFormat;
			else if (Format == DateTimeFormat.LocalMktDate)
				form = LocalMktDateFormat;
			else if (Format == DateTimeFormat.MonthYear)
				form = MonthYear;
			else
				form = UtcDateFormat;
		}

		public DateTimeFieldAttribute(int order, Tag tag, string format)
			: base(order, tag)
		{
			form = format;
		}

		public override byte[] Encode(object value)
		{
			return Encode(Tag, Encoding.UTF8.GetBytes(((DateTime)value).ToString(form)));
		}

		public override object Decode(byte[] data)
		{
			if (Format == DateTimeFormat.MonthYear && data.Length == 8)
				data = data.Take(6).ToArray();
			
			return DateTime.ParseExact(Encoding.UTF8.GetString(data), form, CultureInfo.InvariantCulture);
		}

		public override bool Validate(object value)
		{
			return true;
		}

		public override string ToString(object value)
		{
			return $"{Tag}({(int)Tag})={((DateTime)value).ToString(form)} ";
		}
	}
}