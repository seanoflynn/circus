using System;
using System.Collections.Generic;
using System.Threading;

using Circus.Common;

namespace Circus.Server
{
	public class TradingSessionEvent
	{
		public DateTime Time { get; set; }
		public SecurityTradingStatus Status { get; set; }

		public TradingSessionEvent(DateTime time, SecurityTradingStatus status)
		{
			Time = time;
			Status = status;
		}
	}

	public class TradingSession
	{
		public event EventHandler<SecurityTradingStatus> Changed;

		public List<TradingSessionEvent> Events { get; private set; } = new List<TradingSessionEvent>();

		public SecurityTradingStatus Current { get; private set; } = SecurityTradingStatus.UnknownInvalid;

		private Timer timer;

		public TradingSession()
		{ }

		public TradingSession(DateTime date, TimeSpan preOpen, TimeSpan noCancel, TimeSpan open, TimeSpan close, TimeSpan notAvailable)
		{
			Events.Add(new TradingSessionEvent(date + preOpen, SecurityTradingStatus.PreOpen));
			Events.Add(new TradingSessionEvent(date + noCancel, SecurityTradingStatus.NoChange));
			Events.Add(new TradingSessionEvent(date + open, SecurityTradingStatus.Open));
			Events.Add(new TradingSessionEvent(date + close, SecurityTradingStatus.Close));
			Events.Add(new TradingSessionEvent(date + notAvailable, SecurityTradingStatus.NotAvailable));
		}

		public void Add(DateTime time, SecurityTradingStatus status)
		{
			Events.Add(new TradingSessionEvent(time, status));
		}

		public void Update(SecurityTradingStatus status)
		{
			// only fire event if we've changed state
			if (status == Current)
				return;
			
			Current = status;

            Changed?.Invoke(this, Current);
        }

		public void Update(DateTime time)
		{
			var cur = Events[Events.Count - 1].Status;

			for (int i = Events.Count - 1; i >= 0; i--)
			{				
				if (time >= Events[i].Time)
				{
					cur = Events[i].Status;
					break;
				}
			}

			Update(cur);
		}

		public void Start()
		{
			Current = SecurityTradingStatus.UnknownInvalid;
			SetNextEvent();
		}

		public void Stop()
		{
			timer.Dispose();
		}

		private void SetNextEvent(object state = null)
		{
			DateTime time = DateTime.UtcNow;
			Update(time);

			//Console.WriteLine("time until next=" + TimeSpan.FromMilliseconds(interval));
			timer = new Timer(SetNextEvent, null, GetTimeUntilNext(time), Timeout.Infinite);
		}

		private int GetTimeUntilNext(DateTime time)
		{
			foreach (var ev in Events)
			{
				if (time < ev.Time && ev.Status != Current)
					return Convert.ToInt32((ev.Time - time).TotalMilliseconds);
			}

			return int.MaxValue;
		}
	}
}