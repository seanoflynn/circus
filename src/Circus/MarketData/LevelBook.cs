namespace Circus.MarketData;

public sealed class LevelBook
{
    private readonly SortedDictionary<decimal, (int Quantity, int Count)> _bids =
        new(Comparer<decimal>.Create((a, b) => b.CompareTo(a)));

    private readonly SortedDictionary<decimal, (int Quantity, int Count)> _offers = new();

    private IReadOnlyList<Level> _bidLevels = Array.Empty<Level>();
    private IReadOnlyList<Level> _offerLevels = Array.Empty<Level>();

    public IReadOnlyList<Level> Bids => _bidLevels;

    public IReadOnlyList<Level> Offers => _offerLevels;

    public decimal? BestBid => _bidLevels.Count > 0 ? _bidLevels[0].Price : null;

    public decimal? BestOffer => _offerLevels.Count > 0 ? _offerLevels[0].Price : null;

    public void Reset(LevelsDataEvent snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        _bids.Clear();
        _offers.Clear();

        foreach (var level in snapshot.Bids)
            _bids[level.Price] = (level.Quantity, level.Count);
        foreach (var level in snapshot.Offers)
            _offers[level.Price] = (level.Quantity, level.Count);

        _bidLevels = Materialize(_bids);
        _offerLevels = Materialize(_offers);
    }

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
