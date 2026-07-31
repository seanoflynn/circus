using Circus.Actions;
using Circus.Events;
using Circus.Sequencing;
using Circus.Sessions;
using NUnit.Framework;

namespace Circus.Tests.Sequencing;

// Several books behind the one queue, which is where routing and cross-security order become
// things that can be wrong. SequencerTests covers the queue itself with a single book; nothing
// here re-tests that, and nothing here needed the queue to change.
//
// The properties worth holding: an action reaches the book named on it and no other, dispatch
// order across securities follows time rather than registration or submission, one book's
// interruption is nothing to do with the rest, and every book still sees time run one way even
// though nothing enforces that per book - it falls out of dispatching in global order.
[TestFixture]
public class SequencerRoutingTests
{
    private static readonly DateTime Day = new(2000, 1, 1);

    private static readonly TimeSpan PreOpenAt = new(9, 0, 0);
    private static readonly TimeSpan OpenAt = new(9, 30, 0);
    private static readonly TimeSpan CloseAt = new(17, 0, 0);

    private static readonly TimeSpan PauseFor = TimeSpan.FromMinutes(2);

    private static readonly Security Gold = new("GCZ6", 10, 10);
    private static readonly Security Silver = new("SIZ6", 10, 10);
    private static readonly Security Copper = new("HGZ6", 10, 10);

    // A 5-tick volatility band on a reference of 100, so a trade at 200 breaches it and pauses
    // that book for two minutes. Only gold gets one, so the others carry on regardless.
    private static readonly Security PausingGold = new("GCZ6", 10, 10,
        PriceRestrictions: new PriceRestrictionConfig[] {new VolatilityBand(5, PauseFor)});

    private static MarketSchedule TradingDay() => new(PreOpenAt, OpenAt, CloseAt);

    // A day that does not begin until late in the evening, so the schedule stays out of the way
    // while a test drives its books itself.
    private static MarketSchedule Quiet() =>
        new(new TimeSpan(23, 0, 0), new TimeSpan(23, 15, 0), new TimeSpan(23, 45, 0));

    private static DateTime At(int hour, int minute) => Day.Add(new TimeSpan(hour, minute, 0));

    private static DateTime At(int hour, int minute, int second) =>
        Day.Add(new TimeSpan(hour, minute, second));

    private static CreateLimitOrder Order(Security security, string clientOrderId, Side side,
        decimal price, DateTime time) =>
        new()
        {
            Security = security, Time = time, CompanyId = "Company1", ClientOrderId = clientOrderId,
            OrderValidity = new OrderValidity.Day(), Side = side, Quantity = 5, Price = price
        };

    [Test]
    public void AdvanceTo_RoutesEachActionToTheBookNamedOnIt()
    {
        // arrange
        var gold = new OrderBook(Gold);
        var silver = new OrderBook(Silver);
        var sequencer = new Sequencer(At(12, 0));
        sequencer.Add(gold, Quiet());
        sequencer.Add(silver, Quiet());

        sequencer.Submit(new OpenTrading {Security = Gold, Time = At(12, 0)});
        sequencer.Submit(new OpenTrading {Security = Silver, Time = At(12, 0)});

        // act - one order each, at prices that would cross if they ever met in one book
        sequencer.Submit(Order(Gold, "Gold1", Side.Buy, 100, At(12, 1)));
        sequencer.Submit(Order(Silver, "Silver1", Side.Sell, 100, At(12, 2)));
        var dispatched = sequencer.AdvanceTo(At(13, 0));

        // assert - each order confirmed against its own security, and neither book saw the other's
        var confirmed = dispatched
            .SelectMany(d => d.Events)
            .OfType<CreateOrderConfirmed>()
            .ToList();

        Assert.AreEqual(2, confirmed.Count);
        Assert.AreEqual(Gold.Name, confirmed[0].Security.Name);
        Assert.AreEqual("Gold1", confirmed[0].Order.ClientOrderId);
        Assert.AreEqual(Silver.Name, confirmed[1].Security.Name);
        Assert.AreEqual("Silver1", confirmed[1].Order.ClientOrderId);

        // nothing crossed: two resting orders in two books are not a trade
        Assert.IsEmpty(dispatched.SelectMany(d => d.Events).OfType<OrdersMatched>());
    }

    [Test]
    public void AdvanceTo_AcrossSecurities_DispatchesInTimeOrderNotSubmissionOrder()
    {
        // arrange
        var gold = new OrderBook(Gold);
        var silver = new OrderBook(Silver);
        var sequencer = new Sequencer(At(12, 0));
        sequencer.Add(gold, Quiet());
        sequencer.Add(silver, Quiet());

        sequencer.Submit(new OpenTrading {Security = Gold, Time = At(12, 0)});
        sequencer.Submit(new OpenTrading {Security = Silver, Time = At(12, 0)});

        // act - submitted grouped by security, and deliberately not in time order within either
        sequencer.Submit(Order(Gold, "Gold1", Side.Buy, 100, At(12, 0, 30)));
        sequencer.Submit(Order(Gold, "Gold2", Side.Buy, 100, At(12, 0, 50)));
        sequencer.Submit(Order(Silver, "Silver1", Side.Buy, 100, At(12, 0, 20)));
        sequencer.Submit(Order(Silver, "Silver2", Side.Buy, 100, At(12, 0, 40)));

        var dispatched = sequencer.AdvanceTo(At(13, 0));

        // assert - interleaved by when they happened, whoever submitted them and in what order
        var orders = dispatched
            .Select(d => d.Action)
            .OfType<CreateLimitOrder>()
            .Select(o => o.ClientOrderId)
            .ToList();

        Assert.AreEqual(new[] {"Silver1", "Gold1", "Silver2", "Gold2"}, orders);
    }

    [Test]
    public void AdvanceTo_EveryBookSeesTimeRunOneWay()
    {
        // arrange - three books, so an ordering mistake has somewhere to hide
        var books = new[] {Gold, Silver, Copper}.Select(s => new OrderBook(s)).ToList();
        var sequencer = new Sequencer(At(12, 0));
        foreach (var book in books)
            sequencer.Add(book, Quiet());

        foreach (var book in books)
            sequencer.Submit(new OpenTrading {Security = book.Security, Time = At(12, 0)});

        // act - flow for all three, interleaved and submitted out of time order
        var random = new Random(7);
        for (var i = 0; i < 200; i++)
        {
            var security = books[random.Next(books.Count)].Security;
            sequencer.Submit(Order(security, $"o{i}", random.Next(2) == 0 ? Side.Buy : Side.Sell,
                10 * random.Next(8, 13), At(12, 0).AddMilliseconds(random.Next(60_000))));
        }

        var dispatched = sequencer.AdvanceTo(At(13, 0));

        // assert - per book, the actions it was handed never step backwards. Nothing enforces this
        // per book; it follows from one queue dispatching in global time order, and OrderBook
        // would have thrown during dispatch if it did not hold.
        foreach (var group in dispatched.GroupBy(d => d.Action.Security.Name))
        {
            var times = group.Select(d => d.Action.Time).ToList();
            Assert.AreEqual(times.OrderBy(t => t).ToList(), times,
                $"{group.Key} was handed actions out of time order");
        }

        // and globally, since that is what the per-book property rests on
        var all = dispatched.Select(d => d.Action.Time).ToList();
        Assert.AreEqual(all.OrderBy(t => t).ToList(), all);
    }

    [Test]
    public void AdvanceTo_OneBookPausing_LeavesTheOthersTrading()
    {
        // arrange - only gold carries a volatility band
        var gold = new OrderBook(PausingGold);
        var silver = new OrderBook(Silver);
        var sequencer = new Sequencer(At(12, 0));
        sequencer.Add(gold, Quiet());
        sequencer.Add(silver, Quiet());

        sequencer.Submit(new OpenTrading {Security = PausingGold, Time = At(12, 0), ReferencePrice = 100});
        sequencer.Submit(new OpenTrading {Security = Silver, Time = At(12, 0)});

        // act - gold trades through its band and pauses; silver trades at the same price and does
        // not, having no band to breach
        sequencer.Submit(Order(PausingGold, "Gold1", Side.Sell, 200, At(12, 1)));
        sequencer.Submit(Order(PausingGold, "Gold2", Side.Buy, 200, At(12, 1)));
        sequencer.Submit(Order(Silver, "Silver1", Side.Sell, 200, At(12, 2)));
        sequencer.Submit(Order(Silver, "Silver2", Side.Buy, 200, At(12, 2)));

        var dispatched = sequencer.AdvanceTo(At(12, 2, 30));

        // assert
        Assert.AreEqual(OrderBookStatus.Paused, gold.Status, "gold breached its band");
        Assert.AreEqual(OrderBookStatus.Open, silver.Status, "silver has no band to breach");

        // silver printed while gold was paused, at the price that stopped gold
        var trades = dispatched.SelectMany(d => d.Events).OfType<OrdersMatched>().ToList();
        Assert.AreEqual(1, trades.Count);
        Assert.AreEqual(Silver.Name, trades[0].Security.Name);
        Assert.AreEqual(200, trades[0].Price);

        // and only gold was told anything about a status change
        var statuses = dispatched.SelectMany(d => d.Events).OfType<StatusChanged>()
            .Where(s => s.Reason == StatusChangeReason.PriceRestriction)
            .ToList();
        Assert.AreEqual(1, statuses.Count);
        Assert.AreEqual(Gold.Name, statuses[0].Security.Name);
    }

    [Test]
    public void AdvanceTo_InterruptionTick_PokesOnlyTheBookThatPaused()
    {
        // arrange - gold pauses at 12:01, due back at 12:03
        var gold = new OrderBook(PausingGold);
        var silver = new OrderBook(Silver);
        var sequencer = new Sequencer(At(12, 0));
        sequencer.Add(gold, Quiet());
        sequencer.Add(silver, Quiet());

        sequencer.Submit(new OpenTrading {Security = PausingGold, Time = At(12, 0), ReferencePrice = 100});
        sequencer.Submit(new OpenTrading {Security = Silver, Time = At(12, 0)});
        sequencer.Submit(Order(PausingGold, "Gold1", Side.Sell, 200, At(12, 1)));
        sequencer.Submit(Order(PausingGold, "Gold2", Side.Buy, 200, At(12, 1)));

        // act - past the deadline
        var dispatched = sequencer.AdvanceTo(At(12, 4));

        // assert - exactly one poke, carrying gold's security and nobody else's
        var ticks = dispatched.Select(d => d.Action).OfType<AdvanceTime>().ToList();
        Assert.AreEqual(1, ticks.Count);
        Assert.AreEqual(Gold.Name, ticks[0].Security.Name);
        Assert.AreEqual(At(12, 1) + PauseFor, ticks[0].Time);

        Assert.AreEqual(OrderBookStatus.Open, gold.Status, "gold came back");
        Assert.AreEqual(OrderBookStatus.Open, silver.Status, "silver never left");
    }

    [Test]
    public void AdvanceTo_BooksOnDifferentSchedules_EachFollowsItsOwn()
    {
        // arrange - gold trades the ordinary day, silver an evening session
        var gold = new OrderBook(Gold);
        var silver = new OrderBook(Silver);
        var sequencer = new Sequencer(Day);
        sequencer.Add(gold, TradingDay());
        sequencer.Add(silver, new MarketSchedule(new TimeSpan(18, 0, 0), new TimeSpan(18, 30, 0),
            new TimeSpan(22, 0, 0)));

        // act - past gold's whole day, and only into silver's pre-open
        var dispatched = sequencer.AdvanceTo(At(18, 15));

        // assert - gold opened and closed, silver has only pre-opened
        Assert.AreEqual(OrderBookStatus.Closed, gold.Status);
        Assert.AreEqual(OrderBookStatus.PreOpen, silver.Status);

        var order = dispatched
            .Select(d => (d.Action.Security.Name, Kind: d.Action.GetType().Name, d.Action.Time))
            .ToList();

        Assert.AreEqual(
            new[]
            {
                (Gold.Name, nameof(PreOpenTrading), At(9, 0)),
                (Gold.Name, nameof(OpenTrading), At(9, 30)),
                (Gold.Name, nameof(CloseTrading), At(17, 0)),
                (Silver.Name, nameof(PreOpenTrading), At(18, 0))
            },
            order);
    }

    [Test]
    public void AdvanceTo_TwoBooksOpeningAtTheSameInstant_IsDecidedByRegistrationOrder()
    {
        // arrange - identical schedules, so both boundaries land on the same instant and only the
        // submission counter separates them. Registration is what assigns it.
        var gold = new OrderBook(Gold);
        var silver = new OrderBook(Silver);
        var sequencer = new Sequencer(Day);
        sequencer.Add(gold, TradingDay());
        sequencer.Add(silver, TradingDay());

        // act
        var dispatched = sequencer.AdvanceTo(At(9, 0));

        // assert - a tie at one instant resolves the same way every run, which is the property
        // that matters; that it falls to registration order is how, not why
        Assert.AreEqual(2, dispatched.Count);
        Assert.AreEqual(Gold.Name, dispatched[0].Action.Security.Name);
        Assert.AreEqual(Silver.Name, dispatched[1].Action.Security.Name);
        Assert.AreEqual(dispatched[0].Action.Time, dispatched[1].Action.Time);
    }

    [Test]
    public void AdvanceTo_Sequence_IsOneVenueWideCountAcrossBooks()
    {
        // arrange
        var gold = new OrderBook(Gold);
        var silver = new OrderBook(Silver);
        var sequencer = new Sequencer(At(12, 0));
        sequencer.Add(gold, Quiet());
        sequencer.Add(silver, Quiet());

        sequencer.Submit(new OpenTrading {Security = Gold, Time = At(12, 0)});
        sequencer.Submit(new OpenTrading {Security = Silver, Time = At(12, 0)});
        sequencer.Submit(Order(Gold, "Gold1", Side.Buy, 100, At(12, 1)));
        sequencer.Submit(Order(Silver, "Silver1", Side.Buy, 100, At(12, 2)));

        // act
        var dispatched = sequencer.AdvanceTo(At(13, 0));

        // assert - one run of numbers, not one per book: the count is of dispatches, and dispatch
        // order is the only thing that exists at venue scope rather than per security
        Assert.AreEqual(new long[] {1, 2, 3, 4}, dispatched.Select(d => d.Sequence).ToArray());
    }

    [Test]
    public void AdvanceTo_SameInputs_SameDispatchOrder_AcrossBooks()
    {
        // act - two venues built and fed identically
        var first = RunMixedTrace();
        var second = RunMixedTrace();

        // assert - the dispatch stream is a function of the inputs alone. This is what a journal
        // of it is worth: with several books there is a routing table and a per-book schedule in
        // the way, and neither may leak an ordering of its own into the result.
        Assert.AreEqual(first, second);
    }

    // A venue of three books on two schedules, one of them able to pause, fed a seeded mix of
    // flow. Returned as a flat rendering so two runs can be compared directly.
    private static List<string> RunMixedTrace()
    {
        var books = new[]
        {
            new OrderBook(PausingGold),
            new OrderBook(Silver),
            new OrderBook(Copper)
        };

        var sequencer = new Sequencer(Day);
        sequencer.Add(books[0], TradingDay());
        sequencer.Add(books[1], TradingDay());
        sequencer.Add(books[2], new MarketSchedule(new TimeSpan(8, 0, 0), new TimeSpan(8, 30, 0),
            new TimeSpan(16, 0, 0)));

        sequencer.Submit(new OpenTrading
            {Security = PausingGold, Time = At(9, 45), ReferencePrice = 100});

        var random = new Random(11);
        for (var i = 0; i < 300; i++)
        {
            var security = books[random.Next(books.Length)].Security;

            // A price that will breach gold's band now and then, so an interruption tick joins the
            // mix rather than every dispatch being client flow.
            var price = 10 * random.Next(8, 21);
            sequencer.Submit(Order(security, $"o{i}", random.Next(2) == 0 ? Side.Buy : Side.Sell,
                price, At(10, 0).AddMilliseconds(random.Next(3_600_000))));
        }

        return sequencer.AdvanceTo(At(23, 0))
            .Select(d => $"{d.Sequence} {d.Action.Security.Name} {d.Action.GetType().Name} " +
                         $"{d.Action.Time:O} -> {d.Events.Count}")
            .ToList();
    }
}
