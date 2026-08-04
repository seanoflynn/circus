namespace Circus.MarketData;

// Aggregated depth as a subscriber holds it, rebuilt from the incremental feed.
//
// This is where by-price aggregation belongs now: on the consumer side, kept by whoever wants a
// ladder, rather than inside a producer that had to shadow the book to publish one. A venue's
// feed handler does exactly this, and a participant that only wants the touch can keep one of
// these instead of a book.
//
// Applying a delta is idempotent because the feed is keyed on price, so a consumer that reapplies
// a message it has already seen - recovering from a snapshot, say - lands in the same place
// rather than double-counting. That is the property positional level indices would cost.
//
// Holds only what the feed publishes, which is ten deep. A price that falls out of the window
// arrives as Removed and is dropped here too: a subscriber knows what it was told and no more,
// which is the honest depth for it to trade off.
public sealed class LevelBook
{
    private readonly SortedDictionary<decimal, (int Quantity, int Count)> _bids =
        new(Comparer<decimal>.Create((a, b) => b.CompareTo(a)));

    private readonly SortedDictionary<decimal, (int Quantity, int Count)> _offers = new();

    // Rebuilt on change rather than on read, because a consumer reads the touch far more often
    // than the feed moves it - an agent looks at the best bid on every decision.
    private IReadOnlyList<Level> _bidLevels = Array.Empty<Level>();
    private IReadOnlyList<Level> _offerLevels = Array.Empty<Level>();

    public IReadOnlyList<Level> Bids => _bidLevels;

    public IReadOnlyList<Level> Offers => _offerLevels;

    public decimal? BestBid => _bidLevels.Count > 0 ? _bidLevels[0].Price : null;

    public decimal? BestOffer => _offerLevels.Count > 0 ? _offerLevels[0].Price : null;

    // The whole message or none of it. A book update carrying several levels is one step from one
    // consistent state to another, and applying it a level at a time would leave anything reading
    // this between them looking at a book that never existed - a swept level gone with the
    // aggressor's remainder not yet arrived.
    public void Apply(MarketByPriceDeltaEvent message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var touchedBids = false;
        var touchedOffers = false;

        foreach (var change in message.Changes)
        {
            var side = change.Side == Side.Buy ? _bids : _offers;

            if (change.Action == MarketByPriceDeltaAction.Removed)
                side.Remove(change.Price);
            else
                side[change.Price] = (change.Quantity, change.Count);

            if (change.Side == Side.Buy)
                touchedBids = true;
            else
                touchedOffers = true;
        }

        // Rebuilt once per message rather than per change, so a sweep across a side costs one
        // rebuild however many levels it moved.
        if (touchedBids)
            _bidLevels = Materialize(_bids);
        if (touchedOffers)
            _offerLevels = Materialize(_offers);
    }

    private static IReadOnlyList<Level> Materialize(SortedDictionary<decimal, (int Quantity, int Count)> side)
    {
        if (side.Count == 0)
            return Array.Empty<Level>();

        var levels = new List<Level>(side.Count);
        foreach (var (price, (quantity, count)) in side)
            levels.Add(new Level(price, quantity, count));

        return levels;
    }
}
