using System;

namespace Circus
{
    public abstract record OrderValidity
    {
        public sealed record Day : OrderValidity;
        public sealed record GoodTilCanceled : OrderValidity;

        public sealed record GoodTilDate : OrderValidity
        {
            public required DateOnly Date { get; init; }
        }

        // MinQuantity is meaningful only for FillAndKill (CME's MinQty is documented as an
        // IOC-specific qualifier: if at least this much can fill immediately, the order proceeds
        // as an ordinary FillAndKill; otherwise nothing fills at all, not even a partial below the
        // minimum). Carrying it here rather than as a field elsewhere makes that pairing structural
        // - there's no way to construct a MinQuantity that isn't attached to a FillAndKill.
        public sealed record FillAndKill : OrderValidity
        {
            public int? MinQuantity { get; init; }
        }

        public sealed record FillOrKill : OrderValidity;
    }
}
