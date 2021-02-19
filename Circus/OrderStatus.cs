using System;

namespace Circus
{
    [Flags]
    public enum OrderStatus
    {
        Hidden,
        Working,
        Filled,
        Cancelled,
        Expired,
    }
}