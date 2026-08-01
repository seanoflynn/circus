using Circus.Matching;
using NUnit.Framework;

namespace Circus.Tests.Matching;

// Pro rata distributes an aggressor's quantity proportionally among all orders at the resting
// side's best crossing level. Tests here drive Matcher directly and apply the trades between
// iterations so the loop terminates naturally, matching the pattern in MatcherTests.
[TestFixture]
public class ProRataMatchingAlgorithmTests
{
    private static readonly Instrument Gold = new("GCZ6", 10, 10);

    private static readonly DateTime Early = new(2000, 1, 1, 12, 0, 0);
    private static readonly DateTime Late = new(2000, 1, 1, 12, 1, 0);

    private const long Tick = 100;

    private static InternalOrder Order(long sequenceNumber, Side side, int quantity, DateTime time) =>
        new(sequenceNumber, $"Company{sequenceNumber}", $"Order{sequenceNumber}", Gold, time,
            OrderStatus.Working, OrderType.Limit, new OrderValidity.Day(), side, quantity, Tick, null);

    // Applies each trade so the matcher's loop sees updated quantities and terminates.
    // Removes fully-filled orders from the matcher's ladders, matching OrderBook.ApplyTrade.
    private static List<TradeExecuted> RunWithApply(Matcher matcher, IMatchingAlgorithm algorithm)
    {
        var trades = new List<TradeExecuted>();
        foreach (var outcome in matcher.Run(algorithm, algorithm, _ => null))
        {
            if (outcome is TradeExecuted trade)
            {
                var time = trade.Resting.ModifiedTime;
                if (trade.UsesFullRemainingQuantity)
                {
                    trade.Resting.FillFullSize(time, trade.Quantity);
                    trade.Aggressor.FillFullSize(time, trade.Quantity);
                }
                else
                {
                    trade.Resting.Fill(time, trade.Quantity);
                    trade.Aggressor.Fill(time, trade.Quantity);
                }

                if (trade.Resting.Status == OrderStatus.Filled)
                    matcher.Unrest(trade.Resting);
                if (trade.Aggressor.Status == OrderStatus.Filled)
                    matcher.Unrest(trade.Aggressor);

                trades.Add(trade);
            }
        }
        return trades;
    }

    [Test]
    public void ProRata_DistributesAcrossMultipleOrdersAtTheLevel()
    {
        // Three resting buys at the same price, one aggressor sell crossing them all.
        var matcher = new Matcher();

        var buy1 = Order(1, Side.Buy, 50, Early);
        var buy2 = Order(2, Side.Buy, 30, Early);
        var buy3 = Order(3, Side.Buy, 20, Early);
        matcher.Rest(buy1);
        matcher.Rest(buy2);
        matcher.Rest(buy3);
        matcher.Rest(Order(4, Side.Sell, 50, Late));

        var trades = RunWithApply(matcher, new ProRataMatchingAlgorithm());

        Assert.IsNotEmpty(trades);
        var order1Filled = trades.Where(t => t.Resting == buy1).Sum(t => t.Quantity);
        var order2Filled = trades.Where(t => t.Resting == buy2).Sum(t => t.Quantity);
        var order3Filled = trades.Where(t => t.Resting == buy3).Sum(t => t.Quantity);

        Assert.Greater(order1Filled, 0, "order 1 should receive some allocation");
        Assert.Greater(order2Filled, 0, "order 2 should receive some allocation");
        Assert.Greater(order3Filled, 0, "order 3 should receive some allocation");

        Assert.AreEqual(50, order1Filled + order2Filled + order3Filled);
    }

    [Test]
    public void ProRata_LargerOrder_GetsLargerShare()
    {
        var matcher = new Matcher();

        var large = Order(1, Side.Buy, 60, Early);
        var small = Order(2, Side.Buy, 30, Early);
        matcher.Rest(large);
        matcher.Rest(small);
        matcher.Rest(Order(3, Side.Sell, 50, Late));

        var trades = RunWithApply(matcher, new ProRataMatchingAlgorithm());

        var largeFilled = trades.Where(t => t.Resting == large).Sum(t => t.Quantity);
        var smallFilled = trades.Where(t => t.Resting == small).Sum(t => t.Quantity);

        Assert.Greater(largeFilled, smallFilled,
            "the larger order should receive a larger proportional share");
        Assert.AreEqual(50, largeFilled + smallFilled);
    }

    [Test]
    public void ProRata_EqualOrders_GetEqualShares()
    {
        var matcher = new Matcher();

        var buy1 = Order(1, Side.Buy, 50, Early);
        var buy2 = Order(2, Side.Buy, 50, Early);
        matcher.Rest(buy1);
        matcher.Rest(buy2);
        matcher.Rest(Order(3, Side.Sell, 100, Late));

        var trades = RunWithApply(matcher, new ProRataMatchingAlgorithm());

        var firstFilled = trades.Where(t => t.Resting == buy1).Sum(t => t.Quantity);
        var secondFilled = trades.Where(t => t.Resting == buy2).Sum(t => t.Quantity);

        Assert.AreEqual(50, firstFilled);
        Assert.AreEqual(50, secondFilled);
    }

    [Test]
    public void ProRata_PricesAtRestingLimit()
    {
        var matcher = new Matcher();

        var resting = Order(1, Side.Buy, 10, Early);
        matcher.Rest(resting);
        // Aggressor sell at 200 crosses the resting buy at 100
        matcher.Rest(Order(2, Side.Sell, 5, Late));

        var trades = RunWithApply(matcher, new ProRataMatchingAlgorithm());

        var trade = trades.Single();
        Assert.AreEqual(Tick, trade.PriceTicks,
            "should print at the resting order's limit of 100, not the aggressor's 200");
    }

    [Test]
    public void ProRata_NoIndicativeQuote()
    {
        var algorithm = new ProRataMatchingAlgorithm();
        var matcher = new Matcher();
        matcher.Rest(Order(1, Side.Buy, 10, Early));
        matcher.Rest(Order(2, Side.Sell, 10, Late));

        Assert.IsFalse(algorithm.TryQuoteIndicative(matcher.Working, out _, out _));
    }

    [Test]
    public void ProRata_UsesFullRemainingQuantity()
    {
        var algorithm = new ProRataMatchingAlgorithm();
        Assert.IsTrue(algorithm.UsesFullRemainingQuantity);
    }

    [Test]
    public void ProRata_ChecksTradeRestrictions()
    {
        var algorithm = new ProRataMatchingAlgorithm();
        Assert.IsTrue(algorithm.ChecksTradeRestrictions);
    }
}