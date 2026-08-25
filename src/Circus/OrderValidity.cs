namespace Circus;

public abstract record OrderValidity
{
    public sealed record Day : OrderValidity;
    public sealed record GoodTilCanceled : OrderValidity;

    public sealed record GoodTilDate : OrderValidity
    {
        public required DateOnly Date { get; init; }
    }

    public sealed record ImmediateOrCancel : OrderValidity
    {
        public int? MinQuantity { get; init; }
    }
}
