using Circus.Actions;
using Circus.Time;

namespace Circus.Sequencing;

public sealed class LiveDriver
{
    private readonly Sequencer _sequencer;
    private readonly IClock _clock;

    public LiveDriver(Sequencer sequencer, IClock clock)
    {
        _sequencer = sequencer ?? throw new ArgumentNullException(nameof(sequencer));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public void Submit(OrderBookAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        _sequencer.Submit(action with {Time = _clock.GetCurrentTime()});
    }

    public IReadOnlyList<Dispatched> Tick() => _sequencer.AdvanceTo(_clock.GetCurrentTime());
}
