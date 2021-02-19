using System;
using System.Collections.Generic;
using System.Threading;
using Circus.OrderBook;

namespace Circus
{
    public class TradingSessionEvent
    {
        public DateTime Time { get; }
        public OrderBookStatus Status { get; }

        public TradingSessionEvent(DateTime time, OrderBookStatus status)
        {
            Time = time;
            Status = status;
        }
    }

    public class TradingSession
    {
        public event EventHandler<OrderBookStatus> Changed;

        public List<TradingSessionEvent> Events { get; private set; } = new();

        public OrderBookStatus Current { get; private set; } = OrderBookStatus.Closed;

        private Timer _timer;

        public TradingSession()
        {
        }

        public TradingSession(DateTime date, TimeSpan preOpen, TimeSpan noCancel, TimeSpan open, TimeSpan close)
        {
            // Events.Add(new TradingSessionEvent(date + preOpen, OrderBookStatus.PreOpen));
            // Events.Add(new TradingSessionEvent(date + noCancel, OrderBookStatus.NoChange));
            Events.Add(new TradingSessionEvent(date + open, OrderBookStatus.Open));
            Events.Add(new TradingSessionEvent(date + close, OrderBookStatus.Closed));
        }

        public void Add(DateTime time, OrderBookStatus status)
        {
            Events.Add(new TradingSessionEvent(time, status));
        }

        public void Update(OrderBookStatus status)
        {
            // only fire event if we've changed state
            if (status == Current)
            {
                return;
            }

            Current = status;

            Changed?.Invoke(this, Current);
        }

        public void Update(DateTime time)
        {
            var cur = Events[^1].Status;

            for (var i = Events.Count - 1; i >= 0; i--)
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
            Current = OrderBookStatus.Closed;
            SetNextEvent();
        }

        public void Stop()
        {
            _timer.Dispose();
        }

        private void SetNextEvent(object state = null)
        {
            var time = DateTime.UtcNow;
            Update(time);

            //Console.WriteLine("time until next=" + TimeSpan.FromMilliseconds(interval));
            _timer = new Timer(SetNextEvent, null, GetTimeUntilNext(time), Timeout.Infinite);
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