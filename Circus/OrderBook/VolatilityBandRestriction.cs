using System;
using System.Collections.Generic;

namespace Circus.OrderBook
{
    // A range checked against the prospective trade price rather than the submitted one. A breach
    // interrupts continuous trading into an auction rather than rejecting, Eurex-style.
    //
    // Named for what it is - a volatility interruption - which leaves "daily limit" free for the
    // thing that actually is one: a session-long ceiling trading stops at rather than pauses on.
    //
    // Measured against the trades inside a lookback window, which is Eurex's dynamic range and, at
    // a shorter window, CME's velocity logic. With no window it keeps only the newest trade, which
    // is the plain "within range of the last trade" check.
    internal sealed class VolatilityBandRestriction : IPriceRestriction
    {
        private readonly int _rangeTicks;
        private readonly TimeSpan? _window;
        private readonly TimeSpan? _resumeAfter;
        private readonly int? _extendedRangeTicks;

        // Oldest first, so ageing out is a walk from the front. Holds at most one entry when no
        // window is configured.
        private readonly Queue<(long PriceTicks, DateTime Time)> _recentTrades = new();

        // Used only before anything has traded, or after a status change re-seeds the anchor.
        private long? _sessionPriceTicks;

        internal VolatilityBandRestriction(int rangeTicks, TimeSpan? resumeAfter = null,
            TimeSpan? window = null, int? extendedRangeTicks = null)
        {
            _rangeTicks = rangeTicks;
            _resumeAfter = resumeAfter;
            _window = window;
            _extendedRangeTicks = extendedRangeTicks;
        }

        public RestrictionScope Scope => RestrictionScope.Trade;
        public RestrictionBreachAction OnBreach => RestrictionBreachAction.Pause;

        // Eurex times its volatility interruptions rather than leaving them open; configuring no
        // duration leaves the pause standing until someone ends it.
        public TimeSpan? ResumeAfter => _resumeAfter;

        public bool Allows(long priceTicks, DateTime time) => Within(priceTicks, time, _rangeTicks);

        // The wider range an interruption's would-be closing price is held to. Without one
        // configured an interruption simply ends when its time is up.
        public bool AllowsResumption(long priceTicks, DateTime time) =>
            !_extendedRangeTicks.HasValue || Within(priceTicks, time, _extendedRangeTicks.Value);

        // Not an entry restriction, so it has no say in how a stop is priced.
        public bool AllowsStopSpread(long spreadTicks) => true;

        public void OnTrade(long priceTicks, DateTime time)
        {
            if (!_window.HasValue)
                _recentTrades.Clear();

            _recentTrades.Enqueue((priceTicks, time));
        }

        // Ignored: CME and Eurex both measure volatility against prices that actually traded, not
        // against one an auction is only quoting. The entry band does follow it - each restriction
        // owning its own anchor is what lets the two disagree.
        public void OnIndicativePrice(long? priceTicks)
        {
        }

        // An explicit reference supersedes what the market did before it, so the window goes with
        // it - otherwise a fresh settlement price would be measured against yesterday's trades.
        public void OnSessionChange(long? referencePriceTicks)
        {
            if (!referencePriceTicks.HasValue)
                return;

            _sessionPriceTicks = referencePriceTicks;
            _recentTrades.Clear();
        }

        // Within range of every trade still in the window, or of the session reference when nothing
        // has traded yet. Inactive until one or the other exists.
        //
        // The window cannot come to span more than the range allows, because a trade only prints if
        // it was itself in range of everything then inside it. An interruption is the one thing that
        // can move the price further than that in one go, and one lasting longer than the window -
        // which is the usual configuration - has aged the whole window out before trading resumes.
        private bool Within(long priceTicks, DateTime time, int rangeTicks)
        {
            Evict(time);

            if (_recentTrades.Count == 0)
                return !_sessionPriceTicks.HasValue ||
                       Math.Abs(priceTicks - _sessionPriceTicks.Value) <= rangeTicks;

            foreach (var (tradePriceTicks, _) in _recentTrades)
            {
                if (Math.Abs(priceTicks - tradePriceTicks) > rangeTicks)
                    return false;
            }

            return true;
        }

        // Never empties the queue when a window is configured: the newest trade is kept whatever its
        // age, so a market that has gone quiet is still measured against where it last traded rather
        // than falling back to a stale session reference.
        private void Evict(DateTime time)
        {
            if (!_window.HasValue)
                return;

            var cutoff = time - _window.Value;
            while (_recentTrades.Count > 1 && _recentTrades.Peek().Time < cutoff)
                _recentTrades.Dequeue();
        }
    }
}
