using System;
using System.Collections.Generic;

namespace Circus.OrderBook
{
    // Dense, array-backed replacement for SortedDictionary<long, SortedDictionary<long, InternalOrder>>
    // keyed by price tick: the tick is used directly as an array offset (tick - _minTick) for O(1)
    // level access instead of an O(log n) tree lookup, with a cached best-price index so callers
    // don't need to scan the array to find the touch.
    //
    // This type has no notion of a price band/limit — it grows on demand (like List<T>'s doubling)
    // if an order arrives outside the currently allocated range, so it stays correct even when no
    // session price band was configured, organically sizing itself to whatever ticks actually show
    // up. Reset() is purely an optimization for the common case where the eventual range is known
    // up front (a configured session band): it pre-sizes the array so no growth/copy happens during
    // the session. Band enforcement (rejecting out-of-range orders) is a separate concern handled by
    // InMemoryOrderBook.
    internal sealed class PriceLadder(bool descending)
    {
        private const int InitialRadius = 64;

        // true for sides whose priority order runs from high tick to low tick (Side.Buy in the
        // working book, Side.Sell in the stops book) — mirrors what DescendingComparer did.

        private SortedDictionary<long, InternalOrder>?[] _levels = [];
        private long _minTick;
        private int _bestIndex; // index into _levels of the best (nearest-to-crossing) occupied slot; _levels.Length means "none"
        private int _count;

        public bool Any => _count > 0;

        // Wipes the index and pre-sizes it to [centerTick - radiusTicks, centerTick + radiusTicks].
        // Callers are responsible for relocating (or cancelling) any orders that were resting before
        // calling this — nothing is preserved across a Reset.
        public void Reset(long centerTick, int radiusTicks)
        {
            var length = checked((int) (2L * radiusTicks + 1));
            _levels = new SortedDictionary<long, InternalOrder>?[length];
            _minTick = centerTick - radiusTicks;
            _count = 0;
            _bestIndex = _levels.Length;
        }

        public void Add(long tick, long sequenceNumber, InternalOrder order)
        {
            EnsureCapacity(tick);
            var index = (int) (tick - _minTick);

            if (_levels[index] == null)
            {
                _levels[index] = new SortedDictionary<long, InternalOrder>();
                _count++;
                if (IsBetter(index, _bestIndex))
                {
                    _bestIndex = index;
                }
            }

            _levels[index]!.Add(sequenceNumber, order);
        }

        public void Remove(long tick, long sequenceNumber)
        {
            var index = (int) (tick - _minTick);
            var level = _levels[index] ?? throw new InvalidOperationException("no orders at this price");
            level.Remove(sequenceNumber);

            if (level.Count == 0)
            {
                _levels[index] = null;
                _count--;
                if (index == _bestIndex)
                {
                    AdvanceBest();
                }
            }
        }

        // Removes every order resting at `tick` in one go (used when a stop price level triggers).
        public void RemoveLevel(long tick)
        {
            var index = (int) (tick - _minTick);
            if (_levels[index] == null)
                return;

            _levels[index] = null;
            _count--;
            if (index == _bestIndex)
            {
                AdvanceBest();
            }
        }

        public bool TryGetBest(out long tick, out SortedDictionary<long, InternalOrder> level)
        {
            if (_bestIndex >= _levels.Length || _levels[_bestIndex] == null)
            {
                tick = 0;
                level = null!;
                return false;
            }

            tick = _minTick + _bestIndex;
            level = _levels[_bestIndex]!;
            return true;
        }

        public IEnumerable<(long Tick, SortedDictionary<long, InternalOrder> Level)> EnumerateFromBest()
        {
            var step = descending ? -1 : 1;
            for (var i = _bestIndex; i >= 0 && i < _levels.Length; i += step)
            {
                if (_levels[i] != null)
                {
                    yield return (_minTick + i, _levels[i]!);
                }
            }
        }

        private bool IsBetter(int candidateIndex, int currentBestIndex) =>
            currentBestIndex >= _levels.Length ||
            (descending ? candidateIndex > currentBestIndex : candidateIndex < currentBestIndex);

        private void AdvanceBest()
        {
            var step = descending ? -1 : 1;
            var i = _bestIndex + step;
            while (i >= 0 && i < _levels.Length && _levels[i] == null)
            {
                i += step;
            }

            _bestIndex = (i < 0 || i >= _levels.Length) ? _levels.Length : i;
        }

        private void EnsureCapacity(long tick)
        {
            if (_levels.Length == 0)
            {
                _minTick = tick - InitialRadius;
                _levels = new SortedDictionary<long, InternalOrder>?[InitialRadius * 2 + 1];
                _bestIndex = _levels.Length;
                return;
            }

            var maxTick = _minTick + _levels.Length - 1;
            if (tick >= _minTick && tick <= maxTick)
                return;

            var newMin = _minTick;
            var newMax = maxTick;

            if (tick < _minTick)
            {
                var deficit = _minTick - tick;
                newMin = _minTick - Math.Max(deficit, _levels.Length);
            }

            if (tick > maxTick)
            {
                var deficit = tick - maxTick;
                newMax = maxTick + Math.Max(deficit, _levels.Length);
            }

            Grow(newMin, newMax);
        }

        // Like Reset, but preserves existing levels — used for organic on-demand growth mid-session,
        // as opposed to Reset's deliberate wipe at a session boundary.
        private void Grow(long newMin, long newMax)
        {
            var newLevels = new SortedDictionary<long, InternalOrder>?[checked((int) (newMax - newMin + 1))];

            for (var i = 0; i < _levels.Length; i++)
            {
                if (_levels[i] == null)
                    continue;

                newLevels[(_minTick + i) - newMin] = _levels[i];
            }

            _levels = newLevels;
            _minTick = newMin;

            // Grow is rare (amortized via doubling), so a full recompute here is cheap relative to
            // how infrequently it happens.
            _bestIndex = _levels.Length;
            var step = descending ? -1 : 1;
            for (var i = descending ? _levels.Length - 1 : 0; i >= 0 && i < _levels.Length; i += step)
            {
                if (_levels[i] != null)
                {
                    _bestIndex = i;
                    break;
                }
            }
        }
    }
}
