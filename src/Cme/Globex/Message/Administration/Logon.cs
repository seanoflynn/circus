using System;
using System.Text;

using Circus.Common;
using Circus.Fix;

namespace Circus.Cme
{
	public class Logon : Message
	{
		[IntField(1, Tag.HeartBtInt, 30, 60)]
		public int HeartbeatInterval { get; set; } = 30;

		[BoolField(2, Tag.ResetSeqNumFlag)]
		public bool? ResetSequenceNumber { get; set; }

		[IntField(3, Tag.RawDataLength, 0, int.MaxValue)]
		public int? RawDataLength { get; set; }

		[ByteArrayField(4, Tag.RawData)]
		public byte[] RawData { get; set; }

		[EnumField(5, Tag.EncryptMethod)]
		public EncryptionMethod EncryptionMethod { get; set; } = EncryptionMethod.None;

		[StringField(6, Tag.TradingSystemVersion)]
		public string TradingSystemVersion { get; set; } = "TSV";

		[StringField(7, Tag.ApplicationSystemName)]
		public string ApplicationSystemName { get; set; } = "ASN";

		[StringField(8, Tag.ApplicationSystemVendor)]
		public string ApplicationSystemVendor { get; set; } = "ASV";

		public Logon() : base(MessageType.Logon) { }

		public Logon(string password) : this()
		{
			RawData = Encoding.UTF8.GetBytes(password);
			RawDataLength = RawData.Length;
		}
	}
}
