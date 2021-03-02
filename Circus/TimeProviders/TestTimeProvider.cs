using System;

namespace Circus.TimeProviders
{
    public class TestTimeProvider : ITimeProvider
    {
        private DateTime _time;

        public TestTimeProvider(DateTime now)
        {
            _time = now;
        }

        public void SetCurrentTime(DateTime time)
        {
            _time = time;
        }

        public DateTime GetCurrentTime() => _time;
    }
}