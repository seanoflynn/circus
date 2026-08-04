using Circus.MarketData;
using NUnit.Framework;

namespace Circus.Tests.MarketData;

// A subscriber's ladder, applied straight from constructed messages rather than driven through a
// book - what this has to get right is the applying, and a book would only obscure which message
// produced which state. BookLevelViewTests covers it against a real book.
public class LevelBookTests
{
    private static readonly DateTime Now = new(2000, 1, 1, 12, 0, 0);

    // A message carrying one change, which is what most of these are about. Message() below
    // carries several, for the cases that are about a message being applied as a whole.
    private static MarketByPriceDeltaEvent Delta(Side side, decimal price, int quantity, int count,
        MarketByPriceDeltaAction action, int levelIndex = 1) =>
        Message(new MarketByPriceDelta(side, levelIndex, price, quantity, count, action));

    private static MarketByPriceDeltaEvent Message(params MarketByPriceDelta[] changes) =>
        new("GCZ6", Now, changes);

    [Test]
    public void ANewBook_IsEmpty()
    {
        var book = new LevelBook();

        Assert.IsEmpty(book.Bids);
        Assert.IsEmpty(book.Offers);
        Assert.IsNull(book.BestBid);
        Assert.IsNull(book.BestOffer);
    }

    [Test]
    public void Bids_RunFromTheHighestPriceOutward()
    {
        var book = new LevelBook();
        book.Apply(Delta(Side.Buy, 100, 1, 1, MarketByPriceDeltaAction.Added));
        book.Apply(Delta(Side.Buy, 120, 2, 1, MarketByPriceDeltaAction.Added));
        book.Apply(Delta(Side.Buy, 110, 3, 1, MarketByPriceDeltaAction.Added));

        Assert.AreEqual(new[] {120m, 110m, 100m}, book.Bids.Select(l => l.Price).ToArray());
        Assert.AreEqual(120, book.BestBid);
    }

    [Test]
    public void Offers_RunFromTheLowestPriceOutward()
    {
        var book = new LevelBook();
        book.Apply(Delta(Side.Sell, 200, 1, 1, MarketByPriceDeltaAction.Added));
        book.Apply(Delta(Side.Sell, 180, 2, 1, MarketByPriceDeltaAction.Added));

        Assert.AreEqual(new[] {180m, 200m}, book.Offers.Select(l => l.Price).ToArray());
        Assert.AreEqual(180, book.BestOffer);
    }

    [Test]
    public void Modified_ReplacesTheLevelInPlace()
    {
        var book = new LevelBook();
        book.Apply(Delta(Side.Buy, 100, 3, 1, MarketByPriceDeltaAction.Added));
        book.Apply(Delta(Side.Buy, 100, 9, 4, MarketByPriceDeltaAction.Modified));

        Assert.AreEqual(1, book.Bids.Count);
        Assert.AreEqual(9, book.Bids[0].Quantity);
        Assert.AreEqual(4, book.Bids[0].Count);
    }

    [Test]
    public void Removed_DropsTheLevel()
    {
        var book = new LevelBook();
        book.Apply(Delta(Side.Buy, 100, 3, 1, MarketByPriceDeltaAction.Added));
        book.Apply(Delta(Side.Buy, 110, 3, 1, MarketByPriceDeltaAction.Added));
        book.Apply(Delta(Side.Buy, 110, 0, 0, MarketByPriceDeltaAction.Removed));

        Assert.AreEqual(new[] {100m}, book.Bids.Select(l => l.Price).ToArray());
        Assert.AreEqual(100, book.BestBid);
    }

    [Test]
    public void TheTwoSides_AreKeptApart()
    {
        var book = new LevelBook();
        book.Apply(Delta(Side.Buy, 100, 3, 1, MarketByPriceDeltaAction.Added));
        book.Apply(Delta(Side.Sell, 100, 5, 2, MarketByPriceDeltaAction.Added));

        Assert.AreEqual(3, book.Bids[0].Quantity, "one price, two sides, two levels");
        Assert.AreEqual(5, book.Offers[0].Quantity);
    }

    // The property price keying buys, and the reason the feed does not number its levels
    // positionally: a subscriber that reapplies a message it has already seen - recovering
    // against a snapshot, or reading a retransmission - lands where it already was.
    [Test]
    public void ReapplyingAMessage_ChangesNothing()
    {
        var book = new LevelBook();
        book.Apply(Delta(Side.Buy, 100, 3, 1, MarketByPriceDeltaAction.Added));
        book.Apply(Delta(Side.Buy, 100, 7, 2, MarketByPriceDeltaAction.Modified));
        book.Apply(Delta(Side.Buy, 100, 7, 2, MarketByPriceDeltaAction.Modified));

        Assert.AreEqual(1, book.Bids.Count);
        Assert.AreEqual(7, book.Bids[0].Quantity, "applied twice, counted once");
        Assert.AreEqual(2, book.Bids[0].Count);
    }

    // What batching is for: one message moves the book from one consistent state to another, and
    // a reader never sees the half of it.
    [Test]
    public void AMessageMovingSeveralLevels_AppliesAllOfThem()
    {
        var book = new LevelBook();
        book.Apply(Message(
            new MarketByPriceDelta(Side.Sell, 1, 100, 2, 1, MarketByPriceDeltaAction.Added),
            new MarketByPriceDelta(Side.Sell, 2, 110, 3, 1, MarketByPriceDeltaAction.Added)));

        book.Apply(Message(
            new MarketByPriceDelta(Side.Sell, 1, 100, 0, 0, MarketByPriceDeltaAction.Removed),
            new MarketByPriceDelta(Side.Sell, 1, 110, 0, 0, MarketByPriceDeltaAction.Removed),
            new MarketByPriceDelta(Side.Buy, 1, 90, 4, 1, MarketByPriceDeltaAction.Added)));

        Assert.IsEmpty(book.Offers, "both swept levels gone");
        Assert.AreEqual(new[] {90m}, book.Bids.Select(l => l.Price).ToArray(),
            "and the far side of the same message applied too");
    }

    [Test]
    public void RemovingALevelItNeverHeld_IsHarmless()
    {
        var book = new LevelBook();
        book.Apply(Delta(Side.Buy, 100, 3, 1, MarketByPriceDeltaAction.Added));
        book.Apply(Delta(Side.Buy, 999, 0, 0, MarketByPriceDeltaAction.Removed));

        Assert.AreEqual(new[] {100m}, book.Bids.Select(l => l.Price).ToArray());
    }
}
