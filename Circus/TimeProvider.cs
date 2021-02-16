using System;

namespace Circus
{
    public interface ITimeProvider
    {
        DateTime GetCurrentTime();
    }

    public class UtcTimeProvider : ITimeProvider
    {
        public DateTime GetCurrentTime()
        {
            return DateTime.UtcNow;
        }
    }
    
    public class TestTimeProvider : ITimeProvider
    {
        private DateTime _time;

        public TestTimeProvider(DateTime now)
        {
            _time = now;
        }

        public void SetCurrentTime(DateTime time) => _time = time;

        public DateTime GetCurrentTime() => _time;
    }
}