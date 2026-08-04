using Circus.Events;

namespace Circus.Agents;

// What a participant is holding, built from its own events and nothing else.
//
// Every field here is something the venue said. Nothing in this class processes an action, so it
// cannot quietly disagree with the book about what is resting - and if it ever does, that is a
// bug in the venue's events rather than a difference of opinion between two engines. That is the
// property a private shadow book could never have.
//
// Keyed by ClientOrderId, not ExchangeOrderId - the latter does not stay constant for an order's
// whole life, since a reprice, a quantity increase, or an iceberg peak refilling from its reserve
// all mint a fresh one. The client order id changes too, on every update and cancel, but the
// agent is the one choosing each new value and the venue confirms the rename, so the entry is
// renamed in step rather than needing an id that never moves.
public sealed class OrderTracker
{
    // Insertion-ordered so that picking from it is a function of the order things happened in and
    // not of a hash. Removal reindexes the tail, which is linear - fine for the tens of live
    // orders an agent holds, and worth the ordering it buys.
    private readonly List<string> _liveIds = new();
    private readonly Dictionary<string, int> _liveIndex = new();
    private readonly Dictionary<string, LiveOrder> _live = new();

    private readonly Dictionary<string, int> _positions = new();

    public IReadOnlyList<string> LiveOrderIds => _liveIds;

    public int LiveCount => _liveIds.Count;

    public bool HasLive => _liveIds.Count > 0;

    public LiveOrder this[string clientOrderId] => _live[clientOrderId];

    // Live orders in the order they were first confirmed, oldest first.
    public IEnumerable<LiveOrder> LiveOrders
    {
        get
        {
            foreach (var id in _liveIds)
                yield return _live[id];
        }
    }

    public bool TryGet(string clientOrderId, out LiveOrder? order) => _live.TryGetValue(clientOrderId, out order);

    // One live order, drawn from the given source of randomness. The caller's Random is passed in
    // rather than held, so a seeded agent's whole sequence of decisions comes from its own seed.
    public LiveOrder Pick(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);

        if (_liveIds.Count == 0)
            throw new InvalidOperationException("no live orders to pick from");

        return _live[_liveIds[random.Next(_liveIds.Count)]];
    }

    // Net position in an instrument, signed: bought minus sold. Counted from fills, so it is what
    // the venue says the agent owns rather than what it meant to own.
    public int Position(string symbol) => _positions.GetValueOrDefault(symbol);

    public void Apply(OrderBookEvent ev)
    {
        ArgumentNullException.ThrowIfNull(ev);

        switch (ev)
        {
            case CreateOrderConfirmed created:
                Track(created.Order, previousClientOrderId: null);
                break;

            case UpdateOrderConfirmed updated:
                Track(updated.Order, updated.PreviousClientOrderId);
                break;

            case CancelOrderConfirmed cancelled:
                // By the previous id: the new one named on the cancel was never resting, so it
                // was never tracked.
                Remove(cancelled.PreviousClientOrderId);
                break;

            case ExpireOrderConfirmed expired:
                Remove(expired.Order.ClientOrderId);
                break;

            case FillOrderConfirmed filled:
                _positions[filled.Symbol] = Position(filled.Symbol) +
                                            (filled.Order.Side == Side.Buy ? filled.Quantity : -filled.Quantity);
                Track(filled.Order, previousClientOrderId: null);
                break;

            // Rejections and everything else change nothing here: an order the venue refused is
            // an order that does not exist, and an update it refused leaves the order resting
            // under the id it already had. Both are already what this holds.
        }
    }

    // previousClientOrderId is null for a fresh confirm and the id being renamed from for an
    // update. Renaming - rather than removing and adding under whatever key happens to be there -
    // is what keeps one entry across the chain of client order ids a single resting order
    // accumulates over its life.
    private void Track(Order order, string? previousClientOrderId)
    {
        if (order.RemainingQuantity == 0)
        {
            Remove(previousClientOrderId ?? order.ClientOrderId);
            Remove(order.ClientOrderId);
            return;
        }

        if (previousClientOrderId != null && previousClientOrderId != order.ClientOrderId)
            Remove(previousClientOrderId);

        if (!_liveIndex.ContainsKey(order.ClientOrderId))
        {
            _liveIndex[order.ClientOrderId] = _liveIds.Count;
            _liveIds.Add(order.ClientOrderId);
        }

        _live[order.ClientOrderId] = new LiveOrder(order.Instrument.Symbol, order.CompanyId, order.ClientOrderId,
            order.Side, order.Status, order.Quantity, order.RemainingQuantity, order.DisplayedQuantity,
            order.Price, order.TriggerPrice);
    }

    private void Remove(string clientOrderId)
    {
        if (!_liveIndex.Remove(clientOrderId, out var index))
            return;

        _liveIds.RemoveAt(index);
        _live.Remove(clientOrderId);

        for (var i = index; i < _liveIds.Count; i++)
            _liveIndex[_liveIds[i]] = i;
    }
}
