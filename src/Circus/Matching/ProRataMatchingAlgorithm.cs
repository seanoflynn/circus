namespace Circus.Matching;

// Continuous trading under pro-rata priority. The aggressor's quantity is distributed
// proportionally among all orders at the resting side's best crossing level, with each
// order receiving a share proportional to its own remaining size. Unlike price-time, a
// later-arriving larger order gets a larger allocation than an earlier-arriving smaller
// one at the same price.
//
// Trades print at the resting order's own limit price, so an aggressor whose limit was
// better than the touch gets the improvement. Sized off the full remaining quantity, since
// an iceberg's hidden reserve participates in the allocation.
//
// Allocations for a level are computed up front when the level is first entered, so the
// aggressor's quantity is distributed once across all orders at that level rather than
// iteratively re-allocating to only the head each time.
//
// Not yet reachable from a running book: no TradingPhase constructs it, so nothing an
// Instrument can describe selects it and only the tests drive it directly. Wiring it up means
// letting an instrument name the algorithm its continuous phase runs, which is the change
// IMatchingAlgorithm's "instances rather than singletons" already anticipates. Until then this
// is a complete implementation waiting on that seam rather than dead code.
internal sealed class ProRataMatchingAlgorithm : IMatchingAlgorithm
{
    // Cached allocations for the current price level, consumed one per SelectNext call.
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
        // On the first call, or when the price level changes, compute all allocations for
        // this level up front. The state is discarded once the level is exhausted.
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

    // Walks the level and computes each order's pro-rata share of the aggressor's remaining
    // quantity. The first pass allocates proportionally by remaining size; the remainder
    // (from rounding) is given one lot at a time to the largest orders first.
    private static List<(InternalOrder Order, int Quantity)> ComputeAllocations(
        InternalOrder restingHead, InternalOrder aggressor)
    {
        // Gather all orders at this level.
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

        // First pass: proportional allocation.
        for (var i = 0; i < orders.Count; i++)
        {
            var order = orders[i];
            var share = (int)((long)order.RemainingQuantity * remainingAggressor / totalLevelQty);
            share = Math.Min(share, order.RemainingQuantity);
            allocations[i] = (order, share);
            allocatedTotal += share;
        }

        // Distribute the remainder from rounding, one lot at a time, largest orders first.
        var remainder = (int)(remainingAggressor - allocatedTotal);
        if (remainder > 0)
        {
            // Sort by remaining quantity descending, then by sequence number for stability.
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

        // Filter out zero-quantity allocations and return in FIFO order.
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