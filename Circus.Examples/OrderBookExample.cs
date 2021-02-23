using System;
using System.Linq;
using Circus.OrderBook;

namespace Circus.Examples
{
    public class OrderBookExample
    {
        public static void Run()
        {
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);

            IOrderBook book = new InMemoryOrderBook(sec, new UtcTimeProvider());
            book.OrderBookEvent += (_, args) => args.Events.ToList().ForEach(x => Console.WriteLine(args));

            book.SetStatus(OrderBookStatus.Open);
            book.CreateLimitOrder(Guid.NewGuid(), TimeInForce.Day, Side.Buy, 100, 3);
            book.CreateLimitOrder(Guid.NewGuid(), TimeInForce.Day, Side.Sell, 100, 5);
        }
    }
}   