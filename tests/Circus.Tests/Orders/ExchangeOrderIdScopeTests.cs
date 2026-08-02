using Circus.Actions;
using Circus.Events;
using NUnit.Framework;

namespace Circus.Tests.Orders;

// ExchangeOrderId is unique within an instrument and not beyond it. That is a decision rather than
// an oversight, and it is the kind of decision that gets quietly reversed by someone tidying up,
// so it is pinned here: the collision is asserted, and so is the pair that resolves it.
//
// The alternative - one counter shared by every book - would make each book's ids depend on every
// other book's traffic. A book would stop being reproducible from its own actions alone, and
// replaying one instrument out of a venue-wide journal would stop producing the ids it originally
// issued. Independence is worth more than a tidier id.
[TestFixture]
public class ExchangeOrderIdScopeTests
{
    private static readonly DateTime Now1 = new(2000, 1, 1, 12, 0, 0);

    private static readonly Instrument Gold = new("GCZ6", 10, 10);
    private static readonly Instrument Silver = new("SIZ6", 10, 10);

    [Test]
    public void TwoBooksOpeningOnTheSameDay_IssueTheSameIds()
    {
        // act
        var gold = FirstOrder(Gold);
        var silver = FirstOrder(Silver);

        // assert - the same id in both books, because the counter behind it is seeded from the
        // session date and nothing else
        Assert.AreEqual(gold.ExchangeOrderId, silver.ExchangeOrderId);

        // and the pair is what tells them apart
        Assert.AreNotEqual(
            (gold.Instrument.Symbol, gold.ExchangeOrderId),
            (silver.Instrument.Symbol, silver.ExchangeOrderId));
    }

    [Test]
    public void AnIdCarriesTheDayItWasIssued()
    {
        // arrange
        var day1 = FirstOrder(Gold, new DateTime(2000, 1, 1, 12, 0, 0));
        var day2 = FirstOrder(Gold, new DateTime(2000, 1, 2, 12, 0, 0));

        // assert - seeded from the date, so an id says when as well as which
        Assert.IsTrue(day1.ExchangeOrderId.StartsWith("20000101"), day1.ExchangeOrderId);
        Assert.IsTrue(day2.ExchangeOrderId.StartsWith("20000102"), day2.ExchangeOrderId);
    }

    [Test]
    public void OneBooksIdsAreUnaffectedByAnothersTraffic()
    {
        // arrange - gold alone
        var alone = FirstOrder(Gold);

        // act - gold again, this time with silver taking a hundred orders first. Separate books,
        // so silver's traffic has no counter in common with gold's.
        var silver = Opened(Silver, Now1);
        for (var i = 0; i < 100; i++)
            Rest(silver, Silver, $"S{i}", Now1);

        var alongside = FirstOrder(Gold);

        // assert - the property that makes a book replayable on its own: its ids depend on its own
        // actions and nothing else. A shared counter would break exactly this.
        Assert.AreEqual(alone.ExchangeOrderId, alongside.ExchangeOrderId);
    }

    private static Order FirstOrder(Instrument instrument, DateTime? time = null)
    {
        var at = time ?? Now1;
        return Rest(Opened(instrument, at), instrument, "Order1", at);
    }

    // Through pre-open rather than straight to open: starting a session is what seeds the id
    // counter from the date, and a book taken directly to Open never does it.
    private static OrderBook Opened(Instrument instrument, DateTime at)
    {
        var book = new OrderBook(instrument);
        book.Process(new PreOpenTrading {Symbol = instrument.Symbol, Time = at});
        book.Process(new OpenTrading {Symbol = instrument.Symbol, Time = at});
        return book;
    }

    private static Order Rest(IOrderBook book, Instrument instrument, string clientOrderId, DateTime time)
    {
        var events = book.Process(new CreateLimitOrder
        {
            Symbol = instrument.Symbol, Time = time, CompanyId = "Company1", ClientOrderId = clientOrderId,
            OrderValidity = new OrderValidity.Day(), Side = Side.Buy, Quantity = 5, Price = 100
        });

        return events.OfType<CreateOrderConfirmed>().Single().Order;
    }
}
