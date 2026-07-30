namespace Circus.Sequencing;

// What settles a tie between actions queued at the same instant. A rank, not a category: the
// values matter only in that they are ordered.
//
// It has to exist. In a replay every client action is submitted up front and so carries a lower
// submission counter than any interruption tick queued later, so a counter alone would dispatch
// an order stamped exactly at a resume deadline before the resume - leaving it to meet a paused
// book that should already have reopened.
internal enum DispatchKind
{
    // An open at 09:00:00.000 precedes an order stamped the same instant: the venue decides what
    // a book is doing before anyone trades into it.
    ScheduleTransition,

    // A book coming back from an interruption, for the same reason.
    InterruptionTick,

    // Last, ordered among itself by submission counter.
    ClientFlow
}
