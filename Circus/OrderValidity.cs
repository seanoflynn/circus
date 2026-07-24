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

        // Consolidates what CME's own FIX protocol treats as one concept: IOC (tag 59=3) with an
        // optional MinQty (tag 110) qualifier - there's no separate "FOK" time-in-force at the
        // wire level. MinQuantity null (or less than the order's own Quantity) behaves like
        // classic IOC/FillAndKill: fills whatever's immediately available, cancels the remainder,
        // no minimum required. Setting MinQuantity equal to the order's Quantity reproduces
        // FillOrKill exactly - HasSufficientLiquidity walks the same liquidity Match() would
        // actually consume, so if the gate passes at MinQuantity == Quantity the whole order is
        // guaranteed to fill; if it fails, the order is rejected before ever entering the book.
        // No special-casing needed for either.
        public sealed record ImmediateOrCancel : OrderValidity
        {
            public int? MinQuantity { get; init; }
        }
    }
}
