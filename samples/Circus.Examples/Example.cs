using Circus.DataProducers;
using Circus.OrderBook;
using Circus.TimeProviders;

namespace Circus.Examples;

public class Example
{
    public static void Run()
    {
        var time = new UtcTimeProvider();

        var producer = new TradeDataProducer();

        var sec1 = new Security("GCZ6", SecurityType.Future, 10, 10);
        IOrderBook book1 = new InMemoryOrderBook(sec1, time);

        Print(producer.Process(book1,
            book1.CreateLimitOrder("Buyer", "Order1", new OrderValidity.Day(), Side.Buy, 3, 100)));

        Print(producer.Process(book1,
            book1.CreateLimitOrder("Seller", "Order2", new OrderValidity.Day(), Side.Sell, 5, 100)));
    }

    private static void Print(IEnumerable<TradedDataEvent> events)
    {
        foreach (var @event in events)
        {
            Console.WriteLine(@event);
        }
    }
}
