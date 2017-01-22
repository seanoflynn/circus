using System;
using System.Text;
using System.Collections.Generic;
using System.IO;

using Circus.Common;
using Circus.Server;
using Circus.Fix;

namespace Circus.Cme
{
	public class TradingSessionGroup : Section
	{
		[EnumField(1, Tag.TradingSessionID)]
		public SecurityTradingStatus Status { get; set; }

		[IntField(2, Tag.TradingSessionSubID)]
		public int? NoCancelPeriod { get; set; }

		[DateTimeField(3, Tag.TradSesStartTime, "yyyyMMddHHmmssffffff")]
		public DateTime StartTime { get; set; }
	}

	public class TradeDateGroup : Section
	{
		[DateTimeField(1, Tag.TradeDate, DateTimeFormat.LocalMktDate)]
		public DateTime TradeDate { get; set; }

		[GroupField(2, Tag.NoTradingSessions)]
		public List<TradingSessionGroup> Sessions { get; set; } = new List<TradingSessionGroup>();
	}

	public class TradingSessionList : Section
	{
		[StringField(1, Tag.MsgType)]
		public string Type { get; set; }

		[StringField(2, Tag.ProductComplex)]
		public string ProductComplex { get; set; }

		[IntField(3, Tag.MarketSegmentID)]
		public int MarketSegmentId { get; set; }

		[StringField(4, Tag.SecurityGroup)]
		public string SecurityGroup { get; set; }

		[GroupField(2, Tag.NoDates)]
		public List<TradeDateGroup> Dates { get; set; } = new List<TradeDateGroup>();

		public TradingSession ToTradingSession()
		{
			var ts = new TradingSession();

			foreach (var date in Dates)
			{
				foreach (var sess in date.Sessions)
				{
					// TODO: split out no cancel
					ts.Add(sess.StartTime, sess.Status);
				}
			}

			return ts;
		}
	}

	public class TradingSessionImporter
	{
		public static Dictionary<string,TradingSession> Load(string file)
		{
			var sessions = new Dictionary<string, TradingSession>();

			using (var streamReader = new StreamReader(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read)))
			{
				string line;
				while ((line = streamReader.ReadLine()) != null)
				{
					var x = new TradingSessionList();
					x.Decode(Encoding.UTF8.GetBytes(line));
					sessions.Add(x.SecurityGroup, x.ToTradingSession());
				}
			}

			return sessions;
		}

		public static TradingSession Load(string file, string grp)
		{
			var sessions = new Dictionary<string, TradingSession>();

			using (var streamReader = new StreamReader(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read)))
			{
				string line;
				while ((line = streamReader.ReadLine()) != null)
				{
					var x = new TradingSessionList();
					x.Decode(Encoding.UTF8.GetBytes(line));
					if (x.SecurityGroup == grp)
						return x.ToTradingSession();
				}
			}

			return null;
		}
	}	
}