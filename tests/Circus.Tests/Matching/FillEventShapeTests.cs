using Circus.Events;
using Circus.MarketData;
using Circus.Tests.Helpers;
using NUnit.Framework;

namespace Circus.Tests.Matching;

// A trade is two top-level FillOrderConfirmed events sharing a TradeId, and nothing wraps them.
//
// Pinned here because Trade (the test helper) reassembles the pair for the assertions everywhere
// else, and would go on doing so if the book quietly went back to emitting a composite event.
// The point of the shape is the last test in this file: a participant's feed is a filter, and a
// filter cannot leak a counterparty.
[TestFixture]
public class FillEventShapeTests
{
    private static readonly Instrument Gold = new("GCZ6", 10, 10);
    private static readonly DateTime Open = new(2000, 1, 1, 12, 0, 0);

    // Through PreOpen rather than straight to Open: StartsSession belongs to that phase alone,
    // and the trade id counter is seeded from the session date on the way into it, exactly as
    // the order id counter is - see ExchangeOrderIdScopeTests for that convention. A book opened
    // directly still works, but its ids start from a bare 1 rather than carrying the date.
    private static IOrderBook OpenBook()
    {
        var book = new OrderBook(Gold);
        book.PreOpenTrading(time: Open);
        book.OpenTrading(time: Open);
        return book;
    }

    [Test]
    public void OneTrade_EmitsTwoFills_RestingFirst_SharingOneTradeId()
    {
        var book = OpenBook();
        book.CreateLimitOrder("Resting", "R1", new OrderValidity.Day(), Side.Buy, 5, 100,
            time: Open.AddSeconds(1));

        var events = book.CreateLimitOrder("Aggressor", "A1", new OrderValidity.Day(), Side.Sell, 5, 100,
            time: Open.AddSeconds(2));

        var fills = events.OfType<FillOrderConfirmed>().ToList();
        Assert.AreEqual(2, fills.Count);

        Assert.IsTrue(fills[0].IsResting, "the resting side is emitted first");
        Assert.IsFalse(fills[1].IsResting);
        Assert.AreEqual("Resting", fills[0].CompanyId);
        Assert.AreEqual("Aggressor", fills[1].CompanyId);

        Assert.AreEqual(fills[0].TradeId, fills[1].TradeId, "one trade, one id");
        Assert.AreEqual(100, fills[0].Price);
        Assert.AreEqual(5, fills[0].Quantity);
    }

    [Test]
    public void SeparateTrades_GetSeparateIds()
    {
        var book = OpenBook();
        book.CreateLimitOrder("One", "R1", new OrderValidity.Day(), Side.Buy, 3, 100,
            time: Open.AddSeconds(1));
        book.CreateLimitOrder("Two", "R2", new OrderValidity.Day(), Side.Buy, 3, 90,
            time: Open.AddSeconds(2));

        // Sweeps both levels, so two trades in the one action.
        var events = book.CreateLimitOrder("Aggressor", "A1", new OrderValidity.Day(), Side.Sell, 6, 90,
            time: Open.AddSeconds(3));

        var ids = events.OfType<FillOrderConfirmed>().Select(f => f.TradeId).Distinct().ToList();
        Assert.AreEqual(2, ids.Count);
        Assert.AreEqual(4, events.OfType<FillOrderConfirmed>().Count());
    }

    [Test]
    public void TradeIdsCarryTheSessionDate()
    {
        var book = OpenBook();
        book.CreateLimitOrder("Resting", "R1", new OrderValidity.Day(), Side.Buy, 5, 100,
            time: Open.AddSeconds(1));

        var events = book.CreateLimitOrder("Aggressor", "A1", new OrderValidity.Day(), Side.Sell, 5, 100,
            time: Open.AddSeconds(2));

        // Seeded from the date the session started, exactly as exchange order ids are.
        var id = events.OfType<FillOrderConfirmed>().First().TradeId;
        Assert.IsTrue(id.StartsWith("20000101"), $"expected an id carrying the session date, got {id}");
    }

    [Test]
    public void TradeDataProducer_PublishesOnePrintPerTrade_NotOnePerFill()
    {
        var book = OpenBook();
        var producer = new TradeDataProducer();

        book.CreateLimitOrder("Resting", "R1", new OrderValidity.Day(), Side.Buy, 5, 100,
            time: Open.AddSeconds(1));
        var events = book.CreateLimitOrder("Aggressor", "A1", new OrderValidity.Day(), Side.Sell, 5, 100,
            time: Open.AddSeconds(2));

        var prints = producer.Process(events);

        Assert.AreEqual(2, events.OfType<FillOrderConfirmed>().Count(), "two fills");
        Assert.AreEqual(1, prints.Count, "one public print");
        Assert.AreEqual(100, prints[0].Price);
        Assert.AreEqual(5, prints[0].Quantity);
    }

    // The reason the wrapper had to go. A participant's feed is events filtered to their own
    // CompanyId; with a composite event that filter could only be done by rewriting it, and
    // keeping the event whole handed one participant the other's Order - their client order id,
    // their remaining quantity, their company.
    [Test]
    public void FilteringByCompany_GivesAParticipantTheirOwnFillAndNothingOfTheCounterparty()
    {
        var book = OpenBook();
        book.CreateLimitOrder("Resting", "R1", new OrderValidity.Day(), Side.Buy, 5, 100,
            time: Open.AddSeconds(1));

        var events = book.CreateLimitOrder("Aggressor", "A1", new OrderValidity.Day(), Side.Sell, 5, 100,
            time: Open.AddSeconds(2));

        var mine = events.OfType<OrderEvent>().Where(e => e.CompanyId == "Resting").ToList();

        var myFill = mine.OfType<FillOrderConfirmed>().Single();
        Assert.AreEqual("R1", myFill.ClientOrderId);
        Assert.AreEqual(5, myFill.Quantity);

        // Nothing in the filtered stream mentions the other side at all.
        Assert.IsFalse(mine.Any(e => e.CompanyId == "Aggressor"));
        Assert.IsFalse(mine.OfType<OrderConfirmedEvent>().Any(e => e.Order.CompanyId == "Aggressor"));
        Assert.IsFalse(mine.OfType<OrderConfirmedEvent>().Any(e => e.Order.ClientOrderId == "A1"));
    }
}
