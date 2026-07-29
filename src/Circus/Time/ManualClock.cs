namespace Circus.Time;

public class ManualClock : IClock
{
    private DateTime _time;

    public ManualClock(DateTime now)
    {
        _time = now;
    }

    public void SetCurrentTime(DateTime time)
    {
        _time = time;
    }

    public DateTime GetCurrentTime() => _time;
}
