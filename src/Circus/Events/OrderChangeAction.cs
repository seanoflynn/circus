namespace Circus.Events;

public enum OrderChangeAction
{
    // An order now displayed that was not before - newly rested, a stop that triggered into the
    // working book, or the far side of a requeue.
    Added,

    // A displayed order whose price or size moved without losing its place in the queue.
    Modified,

    // An order no longer displayed - cancelled, expired, fully filled, or the near side of a
    // requeue.
    Removed,

    // Quantity that traded against a displayed order. The order may still be displayed after it.
    Filled
}
