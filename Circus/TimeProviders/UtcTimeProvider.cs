using System;

namespace Circus.TimeProviders
{
    public class UtcTimeProvider : ITimeProvider
    {
        public DateTime GetCurrentTime()
        {
            return DateTime.UtcNow;
        }
    }
}