namespace Circus.Matching;

internal sealed class PriceLadder(bool descending) : IReadOnlyPriceLadder
{
    private const int InitialRadius = 64;

    private InternalOrder?[] _heads = [];
    private InternalOrder?[] _tails = [];
    private int[] _counts = [];

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

    // Takes the tick from the order rather than re-deriving it from Price: Price moves before the
    // ladder does on a reprice, so the two disagree for the length of an update.
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

    public void CopyLevelsFromBest(int maxLevels, List<(long Tick, int Quantity, int Count)> into)
    {
        into.Clear();

        var step = descending ? -1 : 1;

        for (var i = _bestIndex; i >= 0 && i < _heads.Length && into.Count < maxLevels; i += step)
        {
            if (_heads[i] != null)
            {
                into.Add((_minTick + i, _quantities[i], _counts[i]));
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
