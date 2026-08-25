using Circus.Events;

namespace Circus.Agents;

// Keyed by ClientOrderId, not ExchangeOrderId: a reprice, a quantity increase or an iceberg
// peak refilling all mint a fresh exchange id, while the agent renames its own in step.
public sealed class OrderTracker
{
    private readonly List<string> _liveIds = new();
    private readonly Dictionary<string, int> _liveIndex = new();
    private readonly Dictionary<string, LiveOrder> _live = new();

    private readonly Dictionary<string, int> _positions = new();

    public IReadOnlyList<string> LiveOrderIds => _liveIds;

    public int LiveCount => _liveIds.Count;

    public bool HasLive => _liveIds.Count > 0;

    public LiveOrder this[string clientOrderId] => _live[clientOrderId];

    public IEnumerable<LiveOrder> LiveOrders
    {
        get
        {
            foreach (var id in _liveIds)
                yield return _live[id];
        }
    }

    public bool TryGet(string clientOrderId, out LiveOrder? order) => _live.TryGetValue(clientOrderId, out order);

    public LiveOrder Pick(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);

        if (_liveIds.Count == 0)
            throw new InvalidOperationException("no live orders to pick from");

        return _live[_liveIds[random.Next(_liveIds.Count)]];
    }

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
        }
    }

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
