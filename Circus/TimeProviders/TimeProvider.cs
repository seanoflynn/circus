using System;

namespace Circus.TimeProviders
{
    public interface ITimeProvider
    {
        DateTime GetCurrentTime();
    }
}