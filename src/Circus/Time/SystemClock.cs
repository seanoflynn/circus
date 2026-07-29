namespace Circus.Time;

public class SystemClock : IClock
{
    public DateTime GetCurrentTime()
    {
        return DateTime.UtcNow;
    }
}
