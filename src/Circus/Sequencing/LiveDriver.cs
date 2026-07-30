using Circus.Actions;
using Circus.Time;

namespace Circus.Sequencing;

// Drives a sequencer off a clock, and is the only place in a running venue that reads wall-clock
// time. Books hold no clock and the sequencer holds none either; everything downstream is a
// function of actions that already carry their instant, and this is where that instant is
// decided.
//
// Two halves of one job. Submit stamps an arriving action with the time it arrived, the way a
// gateway stamps an inbound message and the matching engine then works off that stamp rather
// than off whatever the clock reads by the time it reaches the message. Tick advances the
// sequencer to whatever the clock now says, dispatching that flow along with any schedule
// boundary or interruption deadline that has come due since the last one.
//
// Nothing here decides ordering - the sequencer does. This only decides what time it is, which
// is the one thing that cannot be derived from the actions in a live venue the way it can in a
// replay.
//
// Single-threaded, like the sequencer it drives: a host calls Tick on a timer from the same
// thread it calls Submit on, and a gateway on an I/O thread hands work across to that thread
// rather than calling in. A clock that jumps backwards - an NTP correction mid-session - will
// be refused by the sequencer rather than quietly reordering the venue, which is the right
// noise to make.
public sealed class LiveDriver
{
    private readonly Sequencer _sequencer;
    private readonly IClock _clock;

    public LiveDriver(Sequencer sequencer, IClock clock)
    {
        _sequencer = sequencer ?? throw new ArgumentNullException(nameof(sequencer));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    // Queues an action at the instant it arrived, stamping over whatever it carried. A client
    // does not get to say when its order reached the exchange, so an action arriving here is
    // stamped rather than trusted - which is also why a pre-stamped trace goes to Replay instead
    // of through this.
    public void Submit(OrderBookAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        _sequencer.Submit(action with {Time = _clock.GetCurrentTime()});
    }

    // Dispatches everything that has come due as of now. Called on a timer by whatever hosts the
    // venue: often enough that an interruption ends near its deadline rather than late, since
    // nothing else is going to poke it.
    public IReadOnlyList<Dispatched> Tick() => _sequencer.AdvanceTo(_clock.GetCurrentTime());
}
