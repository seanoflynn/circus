using Circus.MarketData;

namespace Circus.Tests.Helpers;

// The book a by-order subscriber keeps, rebuilt from the messages it was sent. LevelBook's
// counterpart on the other product: where that holds the ladder a by-price feed publishes, this
// holds the orders a by-order feed publishes and aggregates the ladder itself.
//
// It lives in the tests rather than in the library because of one case it cannot get right. A
// Filled delta says what traded, not what the order displays afterwards, and those differ when a
// print goes through an iceberg's peak and into its reserve - which an auction can do, since it
// fills an order's whole remaining quantity rather than its displayed peak. In continuous trading
// an exhausted peak requeues and the feed says so, as an old id leaving and a new one arriving, so
// a mirror stays exact; through an auction it can silently drift. The answer to that is the
// answer to every other gap in an incremental stream - take the snapshot and start again - which
// Reset is here to do.
//
// Orders are held in arrival order, which is queue order: the feed publishes an arrival at the
// back of its price, and an order that loses priority arrives as a removal and a fresh id rather
// than as a change of place.
internal sealed class OrderBookMirror
{
    private readonly List<RestingOrder> _orders = new();

    public IReadOnlyList<RestingOrder> Orders => _orders;

    // Starts again from the by-order image, discarding whatever was here - the same contract
    // LevelBook.Reset has, and for the same reason.
    public void Reset(OrdersDataEvent snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        _orders.Clear();
        _orders.AddRange(snapshot.Orders);
    }

    // The whole message or none of it, for the reason LevelBook applies one atomically: a message
    // carrying a swept order's departure and the aggressor's remainder is one step between two
    // consistent books, and reading between them is reading a book that never existed.
    public void Apply(MarketByOrderDeltaEvent message)
    {
        ArgumentNullException.ThrowIfNull(message);

        foreach (var change in message.Changes)
        {
            switch (change.Action)
            {
                case MarketByOrderDeltaAction.Added:
                    _orders.Add(new RestingOrder(change.Side, change.ExchangeOrderId, change.Price,
                        change.Quantity));
                    break;

                case MarketByOrderDeltaAction.Modified:
                    Replace(change.ExchangeOrderId,
                        order => order with {Price = change.Price, Quantity = change.Quantity});
                    break;

                case MarketByOrderDeltaAction.Removed:
                    Remove(change.ExchangeOrderId);
                    break;

                // Quantity is what traded, so what is left is what it displayed less that. An
                // order with nothing left has gone: the feed says nothing further about a fully
                // filled order, since leaving is what being filled means.
                case MarketByOrderDeltaAction.Filled:
                    var index = IndexOf(change.ExchangeOrderId);
                    if (index < 0)
                        break;

                    var remaining = _orders[index].Quantity - change.Quantity;
                    if (remaining > 0)
                        _orders[index] = _orders[index] with {Quantity = remaining};
                    else
                        _orders.RemoveAt(index);

                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(message), change.Action, null);
            }
        }
    }

    // The ladder a by-price feed would have published, aggregated here instead. This is the whole
    // relationship between the two products: EMDI is EOBI added up, and a subscriber to the
    // order-by-order feed can arrive at the depth feed's answer without being sent it.
    public IReadOnlyList<Level> Levels(Side side, int depth)
    {
        var byPrice = new SortedDictionary<decimal, (int Quantity, int Count)>(
            side == Side.Buy ? Comparer<decimal>.Create((a, b) => b.CompareTo(a)) : Comparer<decimal>.Default);

        foreach (var order in _orders)
        {
            if (order.Side != side)
                continue;

            byPrice.TryGetValue(order.Price, out var running);
            byPrice[order.Price] = (running.Quantity + order.Quantity, running.Count + 1);
        }

        return byPrice.Take(depth)
            .Select(entry => new Level(entry.Key, entry.Value.Quantity, entry.Value.Count))
            .ToList();
    }

    // Tolerant of an id that is not here, which is not laxity but the shape of the feed: an
    // iceberg whose peak is exhausted reports the fill that emptied it and then the requeue, so
    // the removal names an order the fill has already taken out.
    private void Remove(string exchangeOrderId)
    {
        var index = IndexOf(exchangeOrderId);
        if (index >= 0)
            _orders.RemoveAt(index);
    }

    private void Replace(string exchangeOrderId, Func<RestingOrder, RestingOrder> update)
    {
        var index = IndexOf(exchangeOrderId);
        if (index >= 0)
            _orders[index] = update(_orders[index]);
    }

    private int IndexOf(string exchangeOrderId)
    {
        for (var i = 0; i < _orders.Count; i++)
        {
            if (_orders[i].ExchangeOrderId == exchangeOrderId)
                return i;
        }

        return -1;
    }
}
