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

        var preOpen = new TimeSpan(1, 0, 0);
        var open = new TimeSpan(1, 10, 0);
        var close = new TimeSpan(22, 10, 0);
        var sessionProvider = new SessionProvider(preOpen, open, close);
        sessionProvider.Changed += (_, args) =>
            book.UpdateStatus(args.Status, endsTradingDay: args.EndsTradingDay);
        sessionProvider.Update(new DateTime(2020, 1, 1, 1, 30, 0));

        Print(book.CreateLimitOrder("Buyer", "Order1", new OrderValidity.Day(), Side.Buy, 3, 100));
        Print(book.CreateLimitOrder("Seller", "Order2", new OrderValidity.Day(), Side.Sell, 5, 100));
    }

    public static void BackTestExample()
    {
        var sec = new Instrument("GCZ6", 10, 10);

        // No clock anywhere: a backtest already knows when everything happened, so it stamps
        // each action with the data's own time rather than moving a clock the book would read.
        IOrderBook book = new OrderBook(sec);

        var preOpen = new TimeSpan(1, 0, 0);
        var open = new TimeSpan(1, 10, 0);
        var close = new TimeSpan(22, 10, 0);
        var sessionProvider = new SessionProvider(preOpen, open, close);

        // Stamped at the data's time rather than args.Time. A provider catching up reports
        // boundaries it has already passed, so args.Time can run behind the data being fed -
        // and the book refuses actions that move time backwards.
        var dataTime = new DateTime(2020, 1, 1, 1, 30, 0);
        sessionProvider.Changed += (_, args) =>
            book.UpdateStatus(args.Status, endsTradingDay: args.EndsTradingDay, time: dataTime);

        // loop through data
        for (var i = 0; i < 100; i++)
        {
            var time = dataTime;

            // fires any session boundary the data has passed
            sessionProvider.Update(time);

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

        var preOpen = new TimeSpan(1, 0, 0);
        var open = new TimeSpan(1, 10, 0);
        var close = new TimeSpan(22, 10, 0);
        var sessionProvider = new SessionProvider(preOpen, open, close);
        sessionProvider.Changed += (_, args) =>
            book.UpdateStatus(args.Status, endsTradingDay: args.EndsTradingDay);
        Task.Run(() =>
        {
            var i = 0;
            while (i < 100)
            {
                // this needs to happen on same thread as book is updated
                sessionProvider.Update(clock.GetCurrentTime());
                Thread.Sleep(100);
                i++;
            }
        });

        Print(book.CreateLimitOrder("Buyer", "Order1", new OrderValidity.Day(), Side.Buy, 3, 100));
        Print(book.CreateLimitOrder("Seller", "Order2", new OrderValidity.Day(), Side.Sell, 5, 100));
    }

    private static void Print(IEnumerable<OrderBookEvent> events)
    {
        foreach (var @event in events)
        {
            Console.WriteLine(@event);
        }
    }
}
