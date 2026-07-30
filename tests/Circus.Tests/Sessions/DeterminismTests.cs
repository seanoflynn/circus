using Circus.Actions;
using Circus.Events;
using Circus.Simulator;
using Circus.Time;
using NUnit.Framework;

namespace Circus.Tests.Sessions;

[TestFixture]
public class DeterminismTests
{
    private static readonly Security Sec = new("GCZ6", SecurityType.Future, 10, 10);

    [Test]
    public void SameTraceProducesIdenticalEventStream()
    {
        // Arrange: generate a seeded action sequence
        const int seed = 42;
        const int actionCount = 100;
        var simulator = new OrderFlowSimulator(Sec, seed: seed);
        var actions = simulator.Generate(actionCount);

        // Act: run the same actions through two fresh books
        var events1 = RunTrace(actions, seed);
        var events2 = RunTrace(actions, seed);

        // Assert: both runs produce identical event sequences
        Assert.AreEqual(events1.Count, events2.Count, "Event count mismatch");
        for (int i = 0; i < events1.Count; i++)
        {
            var e1 = events1[i];
            var e2 = events2[i];

            Assert.AreEqual(e1.GetType(), e2.GetType(), $"Event {i}: type mismatch");

            // Events with timestamps must be identical (H1 fix ensures this)
            if (e1 is OrderBookEvent obe1 && e2 is OrderBookEvent obe2)
            {
                Assert.AreEqual(obe1.Security, obe2.Security, $"Event {i}: security mismatch");
                Assert.AreEqual(obe1.Timestamp, obe2.Timestamp,
                    $"Event {i}: timestamp mismatch (H1 fix may not be working)");
            }
        }
    }

    [Test]
    public void DayOrderExpiryOrderIsDeterministic()
    {
        // This tests H2: expiry order by InternalId
        var clock = new ManualClock(new DateTime(2024, 1, 15, 16, 0, 0));
        var book = new OrderBook(Sec, clock);

        // Arrange: open the book and create several day orders
        book.UpdateStatus(OrderBookStatus.Open);

        var order1 = book.Process(new CreateLimitOrder
        {
            Security = Sec, CompanyId = "C1", ClientOrderId = "O1",
            OrderValidity = new OrderValidity.Day(), Side = Side.Buy, Quantity = 1, Price = 100
        });

        var order2 = book.Process(new CreateLimitOrder
        {
            Security = Sec, CompanyId = "C2", ClientOrderId = "O2",
            OrderValidity = new OrderValidity.Day(), Side = Side.Buy, Quantity = 1, Price = 100
        });

        var order3 = book.Process(new CreateLimitOrder
        {
            Security = Sec, CompanyId = "C3", ClientOrderId = "O3",
            OrderValidity = new OrderValidity.Day(), Side = Side.Buy, Quantity = 1, Price = 100
        });

        // Act: close the book, which triggers day order expiry
        var closeEvents = book.Process(new CloseTrading { EndsTradingDay = true });

        // Assert: expiry events come out in InternalId order
        var expireEvents = closeEvents.OfType<ExpireOrderConfirmed>().ToList();
        Assert.AreEqual(3, expireEvents.Count, "Should have 3 expiry events");

        // The orders were created in sequence, so their InternalIds will be in order
        var expireIds = expireEvents.Select(e => e.Order.ClientOrderId).ToList();
        Assert.AreEqual("O1", expireIds[0], "First expiry should be O1");
        Assert.AreEqual("O2", expireIds[1], "Second expiry should be O2");
        Assert.AreEqual("O3", expireIds[2], "Third expiry should be O3");
    }

    private static IReadOnlyList<OrderBookEvent> RunTrace(
        IReadOnlyList<OrderBookAction> actions, int seed)
    {
        var clock = new ManualClock(DateTime.UtcNow);
        var book = new OrderBook(Sec, clock);
        book.UpdateStatus(OrderBookStatus.Open);

        var allEvents = new List<OrderBookEvent>();

        foreach (var action in actions)
        {
            // Advance clock by a small increment to vary timestamp and
            // simulate sequential action processing time
            clock.SetCurrentTime(clock.GetCurrentTime().AddMilliseconds(1));

            var events = book.Process(action);
            allEvents.AddRange(events);
        }

        return allEvents;
    }
}
