namespace Circus.Events;

public enum LevelChangeAction
{
    // A price not previously published, either newly rested at or pushed back into the window.
    Added,

    // A published price whose displayed quantity or order count moved.
    Modified,

    // A price that is no longer published, having emptied or fallen out of the window.
    Removed
}
