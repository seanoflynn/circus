using System;
using Circus.OrderBook;

namespace Circus.SessionProviders
{
    public class SessionProvider : ISessionProvider
    {
        private readonly TimeSpan _preOpenTime;
        private readonly TimeSpan _openTime;
        private readonly TimeSpan _closeTime;
        public event EventHandler<SessionStatusChangedArgs>? Changed;

        private OrderBookStatus? _status;

        private DateTime _nextTime;
        private OrderBookStatus _nextStatus;

        public SessionProvider(TimeSpan preOpenTime, TimeSpan openTime, TimeSpan closeTime)
        {
            if (preOpenTime > openTime) throw new ArgumentException("pre-open must be before open");
            if (openTime > closeTime) throw new ArgumentException("open must be before close");

            _preOpenTime = preOpenTime;
            _openTime = openTime;
            _closeTime = closeTime;
        }

        public void Update(DateTime time)
        {
            if (_status == null)
            {
                _status = OrderBookStatus.Closed;
                Changed?.Invoke(this, new SessionStatusChangedArgs(_status.Value, time));
                Console.WriteLine(time + ": " + _status);
                SetNextTime(time);
            }

            while (time >= _nextTime)
            {
                _status = _nextStatus;
                Changed?.Invoke(this, new SessionStatusChangedArgs(_status.Value, _nextTime));
                Console.WriteLine(time + ": " + _status);
                SetNextTime(time);
            }
        }

        private void SetNextTime(DateTime time)
        {
            switch (_status)
            {
                case OrderBookStatus.Closed:
                {
                    _nextStatus = OrderBookStatus.PreOpen;
                    _nextTime = time.Date.Add(_preOpenTime);

                    if (time.TimeOfDay >= _closeTime)
                    {
                        _nextTime = _nextTime.AddDays(1);
                    }

                    break;
                }
                case OrderBookStatus.PreOpen:
                    _nextStatus = OrderBookStatus.Open;
                    _nextTime = time.Date.Add(_openTime);
                    break;
                default:
                    _nextStatus = OrderBookStatus.Closed;
                    _nextTime = time.Date.Add(_closeTime);
                    break;
            }
        }
    }
}