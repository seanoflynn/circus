using Circus.Matching;
using NUnit.Framework;

namespace Circus.Tests.Matching;

// Drives Matcher directly rather than through OrderBook, which is the only way to run
// it against a matching algorithm the book doesn't itself construct. Covers the seam
// IMatchingAlgorithm.SelectNext opens up: an algorithm choosing a counterparty other than the
// FIFO-earliest order at the level, which is what any non-price-time algorithm (pro-rata and
// friends) needs and what the loop used to decide for itself.
[TestFixture]
public class MatcherTests
{
    private static readonly Instrument Gold = new("GCZ6", 10, 10);

    private static readonly DateTime Early = new(2000, 1, 1, 12, 0, 0);
    private static readonly DateTime Late = new(2000, 1, 1, 12, 1, 0);

    private const long Tick = 100;

    // Nothing yielded by Run is applied here, so the book is never mutated and the loop would
    // re-offer the same crossing forever. Every test below takes only as many outcomes as the
    // lazy iterator has to produce, which is safe precisely because Run yields.
    private static MatchOutcome? FirstOutcome(Matcher matcher, IMatchingAlgorithm algorithm) =>
        matcher.Run(algorithm, algorithm, _ => null).FirstOrDefault();

    private static InternalOrder Order(long sequenceNumber, Side side, int quantity, DateTime time) =>
        new(sequenceNumber, $"Company{sequenceNumber}", $"Order{sequenceNumber}", Gold, time,
            OrderStatus.Working, OrderType.Limit, new OrderValidity.Day(), side, quantity, Tick, null);

    // Three resting buys at one price, then a later sell that crosses all of them - the shape
    // every allocation algorithm differs over.
    private static Matcher BookWithThreeRestingBuys(out InternalOrder first, out InternalOrder second,
        out InternalOrder third, int aggressorQuantity = 50)
    {
        var matcher = new Matcher();

        first = Order(1, Side.Buy, 60, Early);
        second = Order(2, Side.Buy, 30, Early);
        third = Order(3, Side.Buy, 10, Early);

        matcher.Rest(first);
        matcher.Rest(second);
        matcher.Rest(third);
        matcher.Rest(Order(4, Side.Sell, aggressorQuantity, Late));

        return matcher;
    }

    [Test]
    public void SelectNext_ChoosingBehindTheHead_IsHonoured()
    {
        // arrange - the head is offered, as always, but this algorithm allocates to the order
        // behind it. Under the old shape the loop picked the counterparty itself and this was
        // simply not expressible.
        var matcher = BookWithThreeRestingBuys(out var first, out var second, out _);

        // act
        var outcome = FirstOutcome(matcher, new SelectsBehindTheHead());

        // assert
        var trade = outcome as TradeExecuted;
        Assert.IsNotNull(trade);
        Assert.AreSame(second, trade.Resting, "the algorithm's choice should decide the counterparty");
        Assert.AreNotSame(first, trade.Resting, "the FIFO-earliest order should have been passed over");
        Assert.AreEqual(30, trade.Quantity);
    }

    [Test]
    public void PriceTime_TakesTheHeadOfTheLevel()
    {
        // arrange - the default algorithm must still be strict price-time: head first, sized
        // off displayed quantity, printed at the resting order's own limit.
        var matcher = BookWithThreeRestingBuys(out var first, out _, out _);

        // act
        var outcome = FirstOutcome(matcher, new PriceTimeMatchingAlgorithm());

        // assert
        var trade = outcome as TradeExecuted;
        Assert.IsNotNull(trade);
        Assert.AreSame(first, trade.Resting);
        Assert.AreEqual(50, trade.Quantity, "capped by the aggressor's 50, not the head's 60");
        Assert.AreEqual(Tick, trade.PriceTicks);
        Assert.IsFalse(trade.UsesFullRemainingQuantity);
    }

    [Test]
    public void SelectNext_DecliningToMatch_EndsTheRunWithoutTrading()
    {
        // arrange - a crossed book an algorithm refuses to act on should terminate rather than
        // spin, since the loop would otherwise keep re-offering the same crossing.
        var matcher = BookWithThreeRestingBuys(out _, out _, out _);

        // act
        var outcomes = matcher.Run(new DeclinesToMatch(), new PriceTimeMatchingAlgorithm(), _ => null).ToList();

        // assert
        Assert.IsEmpty(outcomes);
    }

    [Test]
    public void TryBegin_ReturningFalse_YieldsNothing()
    {
        // arrange - how an uncrossing pass over an uncrossed book stays a no-op.
        var matcher = BookWithThreeRestingBuys(out _, out _, out _);

        // act
        var outcomes = matcher.Run(new DeclinesToBegin(), new PriceTimeMatchingAlgorithm(), _ => null).ToList();

        // assert
        Assert.IsEmpty(outcomes);
    }

    private sealed class SelectsBehindTheHead : IMatchingAlgorithm
    {
        public bool TryBegin(IReadOnlyDictionary<Side, IReadOnlyPriceLadder> working) => true;

        public Allocation? SelectNext(InternalOrder restingHead, InternalOrder aggressor)
        {
            var behind = restingHead.LevelNext ?? restingHead;
            return new Allocation(behind, Math.Min(behind.RemainingQuantity, aggressor.RemainingQuantity),
                behind.Price!.Value);
        }

        public bool UsesFullRemainingQuantity => true;
        public bool ChecksTradeRestrictions => false;

        public bool TryQuoteIndicative(IReadOnlyDictionary<Side, IReadOnlyPriceLadder> working,
            out long priceTicks, out int quantity)
        {
            priceTicks = 0;
            quantity = 0;
            return false;
        }

        public void OnTrade(long priceTicks)
        {
        }

        public void OnSessionChange(long? referencePriceTicks)
        {
        }
    }

    private sealed class DeclinesToMatch : IMatchingAlgorithm
    {
        public bool TryBegin(IReadOnlyDictionary<Side, IReadOnlyPriceLadder> working) => true;
        public Allocation? SelectNext(InternalOrder restingHead, InternalOrder aggressor) => null;
        public bool UsesFullRemainingQuantity => false;
        public bool ChecksTradeRestrictions => false;

        public bool TryQuoteIndicative(IReadOnlyDictionary<Side, IReadOnlyPriceLadder> working,
            out long priceTicks, out int quantity)
        {
            priceTicks = 0;
            quantity = 0;
            return false;
        }

        public void OnTrade(long priceTicks)
        {
        }

        public void OnSessionChange(long? referencePriceTicks)
        {
        }
    }

    private sealed class DeclinesToBegin : IMatchingAlgorithm
    {
        public bool TryBegin(IReadOnlyDictionary<Side, IReadOnlyPriceLadder> working) => false;
        public Allocation? SelectNext(InternalOrder restingHead, InternalOrder aggressor) =>
            throw new InvalidOperationException("must not be consulted once TryBegin declines");
        public bool UsesFullRemainingQuantity => false;
        public bool ChecksTradeRestrictions => false;

        public bool TryQuoteIndicative(IReadOnlyDictionary<Side, IReadOnlyPriceLadder> working,
            out long priceTicks, out int quantity)
        {
            priceTicks = 0;
            quantity = 0;
            return false;
        }

        public void OnTrade(long priceTicks)
        {
        }

        public void OnSessionChange(long? referencePriceTicks)
        {
        }
    }
}
