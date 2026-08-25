namespace Circus.Sequencing;

// What settles a tie between actions queued at the same instant. The values matter only in that
// they are ordered: a submission counter alone would dispatch an order stamped exactly at a
// resume deadline ahead of the resume, since a replay submits all client flow up front.
internal enum DispatchKind
{
    ScheduleTransition,

    InterruptionTick,

    ClientFlow,

    SnapshotTick
}
