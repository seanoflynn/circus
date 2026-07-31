using Circus.MarketData;
using Circus.Time;

namespace Circus.Examples;

public class MarketDataProducerExample
{
    public static void Run()
    {
        var time = new SystemClock();

        var sec1 = new Security("GCZ6", 10, 10);
        var sec2 = new Security("SIZ6", 10, 10);

        IOrderBook book1 = new TimestampingOrderBook(sec1, time);
        IOrderBook book2 = new TimestampingOrderBook(sec2, time);

        // Both instruments on one channel, the way a venue publishes them: one sequence a
        // subscriber counts to notice it missed something, and each message saying which
        // instrument it is about. A feed per security, because every producer behind one builds
        // its view from a single book's events and none can resync after a missed one.
        var channel = new MarketDataChannel();
        channel.Add(new SecurityFeed(sec1, maxLevels: 10));
        channel.Add(new SecurityFeed(sec2, maxLevels: 10));

        // book1 opens on an auction: orders accumulate in pre-open with the indicative quote
        // tracking them, and the print happens on the way out - the same orders as book2, which
        // opens first and trades them continuously.
        Publish(channel, book1.UpdateStatus(OrderBookStatus.PreOpen));
        Publish(channel,
            book1.CreateLimitOrder("Buyer", "Order1", new OrderValidity.Day(), Side.Buy, 3, 100));
        Publish(channel,
            book1.CreateLimitOrder("Seller", "Order2", new OrderValidity.Day(), Side.Sell, 5, 100));
        Publish(channel, book1.UpdateStatus(OrderBookStatus.Open));

        Publish(channel, book2.UpdateStatus(OrderBookStatus.Open));
        Publish(channel,
            book2.CreateLimitOrder("Buyer", "Order1", new OrderValidity.Day(), Side.Buy, 3, 100));
        Publish(channel,
            book2.CreateLimitOrder("Seller", "Order2", new OrderValidity.Day(), Side.Sell, 5, 100));
    }

    private static void Publish(MarketDataChannel channel, IReadOnlyList<Events.OrderBookEvent> events)
    {
        foreach (var message in channel.Publish(events))
            Console.WriteLine($"{message.Sequence,3} {message.Data.Security.Name} {Describe(message.Data)}");
    }

    // LevelsDataEvent holds lists, which a record's generated ToString renders as type names.
    private static string Describe(MarketDataEvent data) => data switch
    {
        LevelsDataEvent levels =>
            $"LevelsDataEvent {{ Bids = [{string.Join(", ", levels.Bids)}], " +
            $"Offers = [{string.Join(", ", levels.Offers)}] }}",
        _ => data.ToString()!
    };
}
