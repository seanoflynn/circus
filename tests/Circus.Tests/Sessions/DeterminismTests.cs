using System.Text;
using Circus.Actions;
using Circus.Events;
using Circus.Time;
using NUnit.Framework;

namespace Circus.Tests.Sessions;

// A book is a pure function of the actions it is handed. These pin that down, because it is
// what lets a journal of those actions rebuild a book by replaying them - and because the two
// ways it was not true were both invisible until something replayed a trace and compared.
[TestFixture]
public class DeterminismTests
{
    private static readonly Instrument Sec = new("GCZ6", 10, 10);
    private static readonly DateTime Now1 = new(2000, 1, 1, 12, 0, 0);

    [Test]
    public void ReplayingATraceReproducesEveryEventExactly()
    {
        var actions = Trace();

        var first = Replay(actions);
        var second = Replay(actions);

        // Not just the same shape - the same events, timestamps included. A clock read anywhere
        // inside the book would break this, which is the point.
        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void OneActionStampsEveryEventItProducesWithOneInstant()
    {
        var book = new OrderBook(Sec);
        book.Process(new OpenTrading {Symbol = Sec.Symbol, Time = Now1});
        book.Process(new CreateLimitOrder
        {
            Symbol = Sec.Symbol, Time = Now1, CompanyId = "C1", ClientOrderId = "O1",
            OrderValidity = new OrderValidity.Day(), Side = Side.Sell, Quantity = 5, Price = 100
        });

        // A crossing order: confirms, matches, prints, and fills - several events, one action.
        var events = book.Process(new CreateLimitOrder
        {
            Symbol = Sec.Symbol, Time = Now1.AddSeconds(1), CompanyId = "C2", ClientOrderId = "O2",
            OrderValidity = new OrderValidity.Day(), Side = Side.Buy, Quantity = 5, Price = 100
        });

        Assert.Greater(events.Count, 1, "expected a create plus a match");
        Assert.That(
            events.Select(e => e.Time),
            Is.EqualTo(Enumerable.Repeat(Now1.AddSeconds(1), events.Count)));
    }

    [Test]
    public void DayOrdersExpireInIdOrderRatherThanHashOrder()
    {
        var book = new OrderBook(Sec);
        book.Process(new OpenTrading {Symbol = Sec.Symbol, Time = Now1});

        // Enough of them that a dictionary's iteration order would not plausibly match insertion
        // order by chance, and interleaved with cancels so the table has had entries removed.
        var ids = Enumerable.Range(1, 20).Select(i => $"O{i}").ToList();
        foreach (var id in ids)
            Rest(book, id, Now1);

        var cancelled = new[] {"O3", "O7", "O8", "O15"};
        foreach (var id in cancelled)
        {
            book.Process(new CancelOrder
            {
                Symbol = Sec.Symbol, Time = Now1, CompanyId = "C", ClientOrderId = $"{id}x",
                PreviousClientOrderId = id
            });
        }

        var expired = book
            .Process(new CloseTrading {Symbol = Sec.Symbol, Time = Now1.AddHours(6), EndsTradingDay = true})
            .OfType<ExpireOrderConfirmed>()
            .Select(e => e.Order.ClientOrderId)
            .ToList();

        Assert.That(expired, Is.EqualTo(ids.Except(cancelled).ToList()));
    }

    [Test]
    public void AnUnstampedActionIsRefusedRatherThanTreatedAsTheStartOfTime()
    {
        var book = new OrderBook(Sec);

        var ex = Assert.Throws<ArgumentException>(() =>
            book.Process(new OpenTrading {Symbol = Sec.Symbol}));

        Assert.That(ex.Message, Does.Contain("Time"));
    }

    [Test]
    public void TimeRunningBackwardsIsRefused()
    {
        var book = new OrderBook(Sec);
        book.Process(new OpenTrading {Symbol = Sec.Symbol, Time = Now1});

        Assert.Throws<ArgumentException>(() =>
            book.Process(new CloseTrading {Symbol = Sec.Symbol, Time = Now1.AddSeconds(-1)}));

        // The same instant twice is not backwards - a burst can share one.
        Assert.DoesNotThrow(() =>
            book.Process(new AdvanceTime {Symbol = Sec.Symbol, Time = Now1}));
    }

    [Test]
    public void AStampingBookUsesTheClockItWasGiven()
    {
        var clock = new ManualClock(Now1);
        var book = new TimestampingOrderBook(Sec, clock);

        var events = book.OpenTrading();

        Assert.AreEqual(Now1, events.Single().Time);
    }

    // A varied but fixed action sequence: resting orders on both sides, updates, cancels, and
    // orders that cross and print. Built here rather than by OrderFlowSimulator, which lives in
    // a project this one does not reference.
    private static IReadOnlyList<OrderBookAction> Trace()
    {
        var actions = new List<OrderBookAction>();
        var random = new Random(42);
        var time = Now1;
        var live = new List<string>();
        var n = 0;

        for (var i = 0; i < 300; i++)
        {
            time = time.AddMilliseconds(1);
            var roll = random.NextDouble();

            if (live.Count > 0 && roll < 0.2)
            {
                var index = random.Next(live.Count);
                actions.Add(new CancelOrder
                {
                    Symbol = Sec.Symbol, Time = time, CompanyId = "C", ClientOrderId = $"o{n++}",
                    PreviousClientOrderId = live[index]
                });
                live.RemoveAt(index);
                continue;
            }

            if (live.Count > 0 && roll < 0.35)
            {
                var index = random.Next(live.Count);
                var renamed = $"o{n++}";
                actions.Add(new UpdateOrder
                {
                    Symbol = Sec.Symbol, Time = time, CompanyId = "C", ClientOrderId = renamed,
                    PreviousClientOrderId = live[index], NewTotalQuantity = random.Next(1, 10)
                });
                live[index] = renamed;
                continue;
            }

            // Prices straddle 100 so buys and sells cross each other regularly.
            var id = $"o{n++}";
            actions.Add(new CreateLimitOrder
            {
                Symbol = Sec.Symbol, Time = time, CompanyId = "C", ClientOrderId = id,
                OrderValidity = new OrderValidity.Day(),
                Side = random.Next(2) == 0 ? Side.Buy : Side.Sell,
                Quantity = random.Next(1, 10),
                Price = 10 * random.Next(8, 13)
            });
            live.Add(id);
        }

        actions.Add(new CloseTrading {Symbol = Sec.Symbol, Time = time.AddHours(6), EndsTradingDay = true});
        return actions;
    }

    // No clock: the trace carries the time each action happened, which is the property under
    // test. The opening action shares the first action's instant so nothing moves backwards.
    private static List<string> Replay(IReadOnlyList<OrderBookAction> actions)
    {
        var book = new OrderBook(Sec);
        var events = new List<OrderBookEvent>(book.Process(
            new OpenTrading {Symbol = Sec.Symbol, Time = actions[0].Time}));

        foreach (var action in actions)
            events.AddRange(book.Process(action));

        return events.Select(Describe).ToList();
    }

    // Rendered rather than compared directly: OrdersMatched carries its fills in an IList, and a
    // record's generated equality compares that by reference, so two runs producing identical
    // trades would still come out unequal. Expanding the fills here compares what they say.
    private static string Describe(OrderBookEvent e)
    {
        if (e is not OrdersMatched matched)
            return e.ToString();

        var text = new StringBuilder();
        text.Append($"OrdersMatched {{ Time = {matched.Time:O}, Price = {matched.Price}, " +
                    $"Quantity = {matched.Quantity}, Fills = [");
        foreach (var fill in matched.Fills)
            text.Append(fill).Append("; ");
        return text.Append("] }").ToString();
    }

    private static void Rest(IOrderBook book, string clientOrderId, DateTime time) =>
        book.Process(new CreateLimitOrder
        {
            Symbol = Sec.Symbol, Time = time, CompanyId = "C", ClientOrderId = clientOrderId,
            OrderValidity = new OrderValidity.Day(), Side = Side.Buy, Quantity = 1, Price = 100
        });
}
