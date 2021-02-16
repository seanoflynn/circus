using System;

namespace Circus
{
    [Flags]
    public enum OrderStatus
    {
        Hidden,
        Working,
        Filled,
        Deleted,
        Expired,
    }
}