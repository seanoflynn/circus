using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;

using Circus.Common;
using Circus.Fix;

namespace Tests.Fix
{
	public class FixTest
	{
		public FixTest()
		{
			ValueFields();
			NullableValueFieldsSet();
			NullableValueFieldsNull();
			Groups();
		}

		class ValueFieldsTestSection : Section
		{
			[IntField(1, Tag.Price)]
			public int Price { get; set; }

			[StringField(2, Tag.OrigClOrdID)]
			public string OrigClOrdID { get; set; }

			[BoolField(3, Tag.PossResend)]
			public bool PossResend { get; set; }

			[DoubleField(4, Tag.ContractMultiplier)]
			public double ContractMultiplier { get; set; }

			[DateTimeField(5, Tag.SendingTime)]
			public DateTime SendingTime { get; set; }

			[EnumField(6, Tag.Side)]
			public Side Side { get; set; }

			[IntField(7, Tag.RawDataLength)]
			public int RawDataLength { get; set; }

			[ByteArrayField(8, Tag.RawData)]
			public byte[] RawData { get; set; }
		}

		public void ValueFields()
		{
			var temp1 = new ValueFieldsTestSection()
			{
				Price = 12,
				OrigClOrdID = "ABC123",
				PossResend = true,
				ContractMultiplier = -12.212,
				SendingTime = DateTime.Today,
				Side = Side.Buy,
				RawDataLength = 6,
				RawData = new byte[] { 1, 2, 0, 4, 125, 6 },
			};

			var encodedData = temp1.Encode();
			var temp2 = new ValueFieldsTestSection();
			temp2.Decode(encodedData);

			Debug.Assert(temp1.Price == temp2.Price);
			Debug.Assert(temp1.OrigClOrdID == temp2.OrigClOrdID);
			Debug.Assert(temp1.PossResend == temp2.PossResend);
			Debug.Assert(temp1.ContractMultiplier - temp2.ContractMultiplier < 0.00001);
			Debug.Assert(temp1.SendingTime == temp2.SendingTime);
			Debug.Assert(temp1.Side == temp2.Side);
			Debug.Assert(temp1.RawDataLength == temp2.RawDataLength);
			Debug.Assert(temp1.RawData.SequenceEqual(temp2.RawData));
		}

		class NullableValueFieldsTestSection : Section
		{
			[IntField(1, Tag.Price)]
			public int? Price { get; set; }

			[StringField(2, Tag.OrigClOrdID)]
			public string OrigClOrdID { get; set; }

			[BoolField(3, Tag.PossResend)]
			public bool? PossResend { get; set; }

			[DoubleField(4, Tag.ContractMultiplier)]
			public double? ContractMultiplier { get; set; }

			[DateTimeField(5, Tag.SendingTime)]
			public DateTime? SendingTime { get; set; }

			[EnumField(6, Tag.Side)]
			public Side? Side { get; set; }

			[IntField(7, Tag.RawDataLength)]
			public int? RawDataLength { get; set; }

			[ByteArrayField(8, Tag.RawData)]
			public byte[] RawData { get; set; }
		}

		public void NullableValueFieldsSet()
		{
			// with values
			var temp1 = new NullableValueFieldsTestSection()
			{
				Price = 12,
				OrigClOrdID = "ABC123",
				PossResend = true,
				ContractMultiplier = -12.212,
				SendingTime = DateTime.Today,
				Side = Side.Buy,
				RawDataLength = 6,
				RawData = new byte[] { 1, 2, 0, 4, 125, 6 },
			};

			var encodedData = temp1.Encode();
			var temp2 = new NullableValueFieldsTestSection();
			temp2.Decode(encodedData);

			Debug.Assert(temp1.Price == temp2.Price);
			Debug.Assert(temp1.OrigClOrdID == temp2.OrigClOrdID);
			Debug.Assert(temp1.PossResend == temp2.PossResend);
			Debug.Assert(temp1.ContractMultiplier - temp2.ContractMultiplier < 0.00001);
			Debug.Assert(temp1.SendingTime == temp2.SendingTime);
			Debug.Assert(temp1.Side == temp2.Side);
			Debug.Assert(temp1.RawDataLength == temp2.RawDataLength);
			Debug.Assert(temp1.RawData.SequenceEqual(temp2.RawData));
		}

		public void NullableValueFieldsNull()
		{
			// with values
			var temp1 = new NullableValueFieldsTestSection();
			var encodedData = temp1.Encode();

			Debug.Assert(encodedData.Length == 0);

			var temp2 = new NullableValueFieldsTestSection();
			temp2.Decode(encodedData);

			Debug.Assert(temp1.Price == temp2.Price);
			Debug.Assert(temp1.OrigClOrdID == temp2.OrigClOrdID);
			Debug.Assert(temp1.PossResend == temp2.PossResend);
			Debug.Assert(temp1.ContractMultiplier == temp2.ContractMultiplier);
			Debug.Assert(temp1.SendingTime == temp2.SendingTime);
			Debug.Assert(temp1.Side == temp2.Side);
			Debug.Assert(temp1.RawDataLength == temp2.RawDataLength);
			Debug.Assert(temp1.RawData == temp2.RawData);
		}

		public void Groups()
		{
			var temp1 = new Temp()
			{
				Price = 12,
				TempGroups = new List<TempGroup>()
				{
					new TempGroup() { Id = 2, Quantity = 3 },
					new TempGroup() { Id = 4, Quantity = 5 }
				},
				TempSection = new TempSection() { Account = 15, AllocAccount = 20 }
			};
			var encodedData = temp1.Encode();
			var temp2 = new Temp();
			temp2.Decode(encodedData);

			Debug.Assert(temp1.Price == temp2.Price);
			Debug.Assert(temp1.TempGroups.Count == temp2.TempGroups.Count);
			Debug.Assert(temp1.TempGroups[0].Id == temp2.TempGroups[0].Id);
			Debug.Assert(temp1.TempGroups[0].Quantity == temp2.TempGroups[0].Quantity);
			Debug.Assert(temp1.TempGroups[1].Id == temp2.TempGroups[1].Id);
			Debug.Assert(temp1.TempGroups[1].Quantity == temp2.TempGroups[1].Quantity);
			Debug.Assert(temp1.TempSection.Account == temp2.TempSection.Account);
			Debug.Assert(temp1.TempSection.AllocAccount == temp2.TempSection.AllocAccount);
		}
	}

	public class TempGroup : Section
	{
		[IntField(1, Tag.OrderID)]
		public int Id { get; set; }
		[IntField(2, Tag.LastQty)]
		public int Quantity { get; set; }
	}

	public class TempSection : Section
	{
		[IntField(1, Tag.AllocAccount)]
		public int AllocAccount { get; set; }
		[IntField(2, Tag.Account)]
		public int Account { get; set; }
	}

	public class Temp : Section
	{
		[IntField(1, Tag.LastPx)]
		public int Price { get; set; }

		[GroupField(2, Tag.NoOrderIDEntries)]
		public List<TempGroup> TempGroups { get; set; } = new List<TempGroup>();

		[SubsectionField(3)]
		public TempSection TempSection { get; set; } = new TempSection();
	}
}
