namespace Circus.Matching;

internal sealed class ProRataMatchingAlgorithm : IMatchingAlgorithm
{
    private List<(InternalOrder Order, int Quantity)>? _pending;
    private int _index;
    private long? _currentPrice;

    public bool TryBegin(IReadOnlyDictionary<Side, IReadOnlyPriceLadder> working) => true;

    public bool TryQuoteIndicative(IReadOnlyDictionary<Side, IReadOnlyPriceLadder> working,
        out long priceTicks, out int quantity)
    {
        priceTicks = 0;
        quantity = 0;
        return false;
    }

    public Allocation? SelectNext(InternalOrder restingHead, InternalOrder aggressor)
    {
        if (restingHead.Price != _currentPrice || _pending == null)
        {
            _pending = ComputeAllocations(restingHead, aggressor);
            _index = 0;
            _currentPrice = restingHead.Price;
        }

        if (_index >= _pending.Count)
            return null;

        var (order, quantity) = _pending[_index++];
        return new Allocation(order, quantity,
            order.Price ?? throw new InvalidOperationException("limit order requires price"));
    }

    private static List<(InternalOrder Order, int Quantity)> ComputeAllocations(
        InternalOrder restingHead, InternalOrder aggressor)
    {
        var orders = new List<InternalOrder>();
        long totalLevelQty = 0;
        for (var order = restingHead;
             order != null && order.Price == restingHead.Price;
             order = order.LevelNext)
        {
            if (order.RemainingQuantity > 0)
            {
                orders.Add(order);
                totalLevelQty += order.RemainingQuantity;
            }
        }

        if (totalLevelQty == 0 || aggressor.RemainingQuantity == 0)
            return new List<(InternalOrder, int)>();

        var remainingAggressor = aggressor.RemainingQuantity;
        var allocations = new (InternalOrder Order, int Quantity)[orders.Count];
        long allocatedTotal = 0;

        for (var i = 0; i < orders.Count; i++)
        {
            var order = orders[i];
            var share = (int)((long)order.RemainingQuantity * remainingAggressor / totalLevelQty);
            share = Math.Min(share, order.RemainingQuantity);
            allocations[i] = (order, share);
            allocatedTotal += share;
        }

        var remainder = (int)(remainingAggressor - allocatedTotal);
        if (remainder > 0)
        {
            var indices = Enumerable.Range(0, orders.Count)
                .OrderByDescending(i => orders[i].RemainingQuantity)
                .ThenBy(i => orders[i].SequenceNumber)
                .ToList();

            for (var r = 0; r < remainder && r < indices.Count; r++)
            {
                var i = indices[r];
                var maxExtra = Math.Min(orders[i].RemainingQuantity - allocations[i].Quantity, 1);
                if (maxExtra > 0)
                {
                    allocations[i] = (allocations[i].Order, allocations[i].Quantity + 1);
                }
            }
        }

        return allocations.Where(a => a.Quantity > 0)
            .OrderBy(a => a.Order.SequenceNumber)
            .Select(a => (a.Order, a.Quantity))
            .ToList();
    }

    public bool UsesFullRemainingQuantity => true;

    public bool ChecksTradeRestrictions => true;

    public void OnTrade(long priceTicks)
    {
    }

    public void OnSessionChange(long? referencePriceTicks)
    {
    }
}