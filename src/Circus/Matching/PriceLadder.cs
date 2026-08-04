namespace Circus.Matching;

// Dense, array-backed replacement for SortedDictionary<long, SortedDictionary<long, InternalOrder>>
// keyed by price tick: the tick is used directly as an array offset (tick - _minTick) for O(1)
// level access instead of an O(log n) tree lookup, with a cached best-price index so callers
// don't need to scan the array to find the touch.
//
// Each level is an intrusive doubly-linked list threaded through InternalOrder.LevelNext/Prev
// rather than a LinkedList<T>, which would allocate a node wrapper per order - the very cost
// this exists to avoid. A newly-touched level costs nothing but flipping array slots.
//
// Grows on demand if an order arrives outside the allocated range, so it needs no pre-sizing.
//
// descending is true for sides whose priority runs from high tick to low - Side.Buy in the
// working book, Side.Sell in the stops book.
internal sealed class PriceLadder(bool descending) : IReadOnlyPriceLadder
{
    private const int InitialRadius = 64;

    private InternalOrder?[] _heads = [];
    private InternalOrder?[] _tails = [];
    private int[] _counts = [];

    // Sum of DisplayedQuantity across the level, maintained incrementally rather than walked on
    // demand: a market data snapshot is taken far more often than a level is deep, and the top of
    // a busy book carries enough orders that summing it per publish is the more expensive half of
    // the trade. Never RemainingQuantity - an iceberg's hidden reserve is not on the book.
    private int[] _quantities = [];

    private long _minTick;
    private int _bestIndex; // index of the best (nearest-to-crossing) occupied slot; _heads.Length means "none"

    public void Add(long tick, InternalOrder order)
    {
        EnsureCapacity(tick);
        var index = (int) (tick - _minTick);

        var tail = _tails[index];
        order.LevelPrev = tail;
        order.LevelNext = null;

        if (tail != null)
        {
            tail.LevelNext = order;
        }
        else
        {
            _heads[index] = order;
            if (IsBetter(index, _bestIndex))
            {
                _bestIndex = index;
            }
        }

        _tails[index] = order;
        _counts[index]++;
        _quantities[index] += order.DisplayedQuantity;

        order.RestingTick = tick;
    }

    // Takes the tick from the order rather than the caller. Price moves before the ladder does on
    // a reprice, so re-deriving it here would read the price the order is moving *to* while it is
    // still filed under the one it is moving from - which the call order in OrderBook.UpdateOrder
    // is careful to avoid, but carefully rather than structurally. Reading back what Add filed it
    // under cannot get that wrong, and the level aggregate below now depends on it not being.
    public void Remove(InternalOrder order)
    {
        var index = (int) (order.RestingTick - _minTick);

        if (order.LevelPrev != null)
            order.LevelPrev.LevelNext = order.LevelNext;
        else
            _heads[index] = order.LevelNext;

        if (order.LevelNext != null)
            order.LevelNext.LevelPrev = order.LevelPrev;
        else
            _tails[index] = order.LevelPrev;

        order.LevelNext = null;
        order.LevelPrev = null;
        _counts[index]--;
        _quantities[index] -= order.DisplayedQuantity;

        if (_heads[index] == null && index == _bestIndex)
        {
            AdvanceBest();
        }
    }

    // Corrects a level after a resting order's displayed size moved under it - a fill, an auction
    // print re-deriving the peak, or an update resizing the order in place. Reached through
    // Matcher.SyncDisplayed, which is what the callers actually hold.
    internal void AdjustQuantity(long tick, int delta) => _quantities[(int) (tick - _minTick)] += delta;

    public bool TryGetBest(out long tick, out InternalOrder? firstOrder)
    {
        if (_bestIndex >= _heads.Length || _heads[_bestIndex] == null)
        {
            tick = 0;
            firstOrder = null;
            return false;
        }

        tick = _minTick + _bestIndex;
        firstOrder = _heads[_bestIndex];
        return true;
    }

    // Yields, from best outward, each occupied level's tick, its first (FIFO-earliest) order —
    // walk `.LevelNext` from there to see the rest — and its order count.
    public IEnumerable<(long Tick, InternalOrder First, int Count)> EnumerateFromBest()
    {
        var step = descending ? -1 : 1;
        for (var i = _bestIndex; i >= 0 && i < _heads.Length; i += step)
        {
            if (_heads[i] != null)
            {
                yield return (_minTick + i, _heads[i]!, _counts[i]);
            }
        }
    }

    // The aggregate view, from best outward: what a by-price feed publishes, and deliberately
    // separate from EnumerateFromBest so matching keeps walking orders and market data never
    // needs to. Stops at maxLevels occupied levels rather than scanning the whole array, which is
    // what makes this cheap enough to call on every publish.
    public IEnumerable<(long Tick, int Quantity, int Count)> EnumerateLevelsFromBest(int maxLevels)
    {
        var step = descending ? -1 : 1;
        var found = 0;

        for (var i = _bestIndex; i >= 0 && i < _heads.Length && found < maxLevels; i += step)
        {
            if (_heads[i] != null)
            {
                found++;
                yield return (_minTick + i, _quantities[i], _counts[i]);
            }
        }
    }

    private bool IsBetter(int candidateIndex, int currentBestIndex) =>
        currentBestIndex >= _heads.Length ||
        (descending ? candidateIndex > currentBestIndex : candidateIndex < currentBestIndex);

    private void AdvanceBest()
    {
        var step = descending ? -1 : 1;
        var i = _bestIndex + step;
        while (i >= 0 && i < _heads.Length && _heads[i] == null)
        {
            i += step;
        }

        _bestIndex = (i < 0 || i >= _heads.Length) ? _heads.Length : i;
    }

    private void EnsureCapacity(long tick)
    {
        if (_heads.Length == 0)
        {
            _minTick = tick - InitialRadius;
            var length = InitialRadius * 2 + 1;
            _heads = new InternalOrder?[length];
            _tails = new InternalOrder?[length];
            _counts = new int[length];
            _quantities = new int[length];
            _bestIndex = _heads.Length;
            return;
        }

        var maxTick = _minTick + _heads.Length - 1;
        if (tick >= _minTick && tick <= maxTick)
            return;

        var newMin = _minTick;
        var newMax = maxTick;

        if (tick < _minTick)
        {
            var deficit = _minTick - tick;
            newMin = _minTick - Math.Max(deficit, _heads.Length);
        }

        if (tick > maxTick)
        {
            var deficit = tick - maxTick;
            newMax = maxTick + Math.Max(deficit, _heads.Length);
        }

        Grow(newMin, newMax);
    }

    // Preserves existing levels while growing the backing arrays to cover [newMin, newMax].
    private void Grow(long newMin, long newMax)
    {
        var newLength = checked((int) (newMax - newMin + 1));
        var newHeads = new InternalOrder?[newLength];
        var newTails = new InternalOrder?[newLength];
        var newCounts = new int[newLength];
        var newQuantities = new int[newLength];

        for (var i = 0; i < _heads.Length; i++)
        {
            if (_heads[i] == null)
                continue;

            var newIndex = (int) ((_minTick + i) - newMin);
            newHeads[newIndex] = _heads[i];
            newTails[newIndex] = _tails[i];
            newCounts[newIndex] = _counts[i];
            newQuantities[newIndex] = _quantities[i];
        }

        _heads = newHeads;
        _tails = newTails;
        _counts = newCounts;
        _quantities = newQuantities;
        _minTick = newMin;

        // Grow is rare (amortized via doubling), so a full recompute here is cheap relative to
        // how infrequently it happens.
        _bestIndex = _heads.Length;
        var step = descending ? -1 : 1;
        for (var i = descending ? _heads.Length - 1 : 0; i >= 0 && i < _heads.Length; i += step)
        {
            if (_heads[i] != null)
            {
                _bestIndex = i;
                break;
            }
        }
    }
}
