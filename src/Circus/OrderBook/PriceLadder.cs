namespace Circus.OrderBook;

// All anything outside Matcher is handed. Writes go through Matcher's own Rest/Unrest/Reprice,
// so the ladders it owns cannot be mutated from outside.
internal interface IReadOnlyPriceLadder
{
    bool TryGetBest(out long tick, out InternalOrder? firstOrder);

    IEnumerable<(long Tick, InternalOrder First, int Count)> EnumerateFromBest();
}

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
    }

    public void Remove(long tick, InternalOrder order)
    {
        var index = (int) (tick - _minTick);

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

        if (_heads[index] == null && index == _bestIndex)
        {
            AdvanceBest();
        }
    }

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

        for (var i = 0; i < _heads.Length; i++)
        {
            if (_heads[i] == null)
                continue;

            var newIndex = (int) ((_minTick + i) - newMin);
            newHeads[newIndex] = _heads[i];
            newTails[newIndex] = _tails[i];
            newCounts[newIndex] = _counts[i];
        }

        _heads = newHeads;
        _tails = newTails;
        _counts = newCounts;
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
