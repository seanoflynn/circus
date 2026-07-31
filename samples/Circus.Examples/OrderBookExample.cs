using Circus.Actions;
using Circus.Events;
using Circus.Sessions;
using Circus.Time;

namespace Circus.Examples;

public static class OrderBookExample
{
    public static void TestExample()
    {
        var sec = new Instrument("GCZ6", 10, 10);

        var clock = new ManualClock(DateTime.Now);
        IOrderBook book = new TimestampingOrderBook(sec, clock);

        // Open the book for trading before placing orders.
        book.PreOpenTrading(referencePrice: 100);
        book.OpenTrading(referencePrice: 100);

        Print(book.CreateLimitOrder("Buyer", "Order1", new OrderValidity.Day(), Side.Buy, 3, 100));
        Print(book.CreateLimitOrder("Seller", "Order2", new OrderValidity.Day(), Side.Sell, 5, 100));
    }

    public static void BackTestExample()
    {
        var sec = new Instrument("GCZ6", 10, 10);

        // No clock anywhere: a backtest already knows when everything happened, so it stamps
        // each action with the data's own time rather than moving a clock the book would read.
        IOrderBook book = new OrderBook(sec);

        var schedule = new MarketSchedule(new TimeSpan(1, 0, 0), new TimeSpan(1, 10, 0),
            new TimeSpan(22, 10, 0));

        // Advance the book through the schedule to the data's first timestamp.
        var dataTime = new DateTime(2020, 1, 1, 1, 30, 0);
        DriveTo(book, schedule, dataTime);

        // loop through data
        for (var i = 0; i < 100; i++)
        {
            var time = dataTime;

            // pass in data - each order needs its own ClientOrderId, since Buyer's ids are
            // permanently reserved once used
            Print(book.CreateLimitOrder("Buyer", $"Order{i}", new OrderValidity.Day(), Side.Buy, 3, 100,
                time: time));
        }
    }

    public static void LiveExample()
    {
        var sec = new Instrument("GCZ6", 10, 10);

        var clock = new SystemClock();
        IOrderBook book = new TimestampingOrderBook(sec, clock);

        var schedule = new MarketSchedule(new TimeSpan(1, 0, 0), new TimeSpan(1, 10, 0),
            new TimeSpan(22, 10, 0));

        Task.Run(() =>
        {
            var i = 0;
            while (i < 100)
            {
                // this needs to happen on same thread as book is updated
                DriveTo(book, schedule, clock.GetCurrentTime());
                Thread.Sleep(100);
                i++;
            }
        });

        Print(book.CreateLimitOrder("Buyer", "Order1", new OrderValidity.Day(), Side.Buy, 3, 100));
        Print(book.CreateLimitOrder("Seller", "Order2", new OrderValidity.Day(), Side.Sell, 5, 100));
    }

    // Walk the schedule from the start of the day to the given time, applying every transition
    // the book has passed. Idempotent: calling again with the same or earlier time is a no-op.
    private static void DriveTo(IOrderBook book, MarketSchedule schedule, DateTime time)
    {
        var t = time.Date;
        while (true)
        {
            var next = schedule.NextAfter(t);
            if (next == null || next.Value.Time > time) break;
            book.UpdateStatus(next.Value.Status, endsTradingDay: next.Value.EndsTradingDay,
                time: next.Value.Time);
            t = next.Value.Time;
        }
    }

    private static void Print(IEnumerable<OrderBookEvent> events)
    {
        foreach (var @event in events)
        {
            Console.WriteLine(@event);
        }
    }
}