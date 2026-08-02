using Circus.Events;

namespace Circus.Examples;

// The smallest thing the library does: actions in, events out.
//
// A bare OrderBook and no clock anywhere. Every action carries the instant it happened at, so
// the book needs nothing ambient to decide what an action means - which is why running this
// twice prints the same thing twice, and why a journal of these actions is enough to rebuild
// the book later with nothing recorded beside it.
public static class OrderBookExample
{
    private static readonly Instrument Gold = new("GCZ6", TickSize: 10);

    private static readonly DateTime Open = new(2026, 1, 5, 9, 0, 0, DateTimeKind.Utc);

    public static void Run()
    {
        var book = new OrderBook(Gold);

        Print(book.OpenTrading(referencePrice: 1000, time: Open));

        // Rests: there is nothing on the other side to trade with yet.
        Print(book.CreateLimitOrder("Buyer", "B1", new OrderValidity.Day(), Side.Buy, 3, 1000,
            time: Open.AddSeconds(1)));

        // Crosses. One action, several events - the confirm, the match, a fill for each side -
        // and every one of them carrying this action's instant rather than a fresh clock read.
        // The seller is left resting the two lots that found no buyer.
        Print(book.CreateLimitOrder("Seller", "S1", new OrderValidity.Day(), Side.Sell, 5, 1000,
            time: Open.AddSeconds(2)));
    }

    private static void Print(IEnumerable<OrderBookEvent> events)
    {
        foreach (var ev in events)
            Console.WriteLine($"  {ev}");
    }
}
