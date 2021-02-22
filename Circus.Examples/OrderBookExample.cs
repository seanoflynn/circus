using System;
using System.Threading;
using Circus.OrderBook;

namespace Circus.Examples
{
    public class OrderBookExample
    {
        public static void Run()
        {
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);

            var book = new OrderBook.OrderBook(sec, new UtcTimeProvider());
            // book.Traded += (o, e) => { Console.WriteLine("traded qty=" + e.Fills[0].Quantity); };
            book.SetStatus(OrderBookStatus.Open);

            book.CreateLimitOrder(Guid.NewGuid(), TimeInForce.Day, Side.Buy, 100, 3);
            var events = book.CreateLimitOrder(Guid.NewGuid(), TimeInForce.Day, Side.Sell, 100, 5);
        }
    }
}