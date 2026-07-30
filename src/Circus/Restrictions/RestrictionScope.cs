namespace Circus.Restrictions;

// Flags, because a daily price limit is both: it refuses an order priced beyond the limit and
// refuses to print through it. Everything else governs one or the other.
[Flags]
internal enum RestrictionScope
{
    OrderEntry = 1,
    Trade = 2
}
