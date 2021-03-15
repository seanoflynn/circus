using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Circus.OrderBook;
using Circus.SessionProviders;
using Circus.TimeProviders;

namespace Circus.Examples
{
    public static class OrderBookExample
    {
        public static void TestExample()
        {
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);

            var timeProvider = new TestTimeProvider(DateTime.Now);
            IOrderBook book = new InMemoryOrderBook(sec, timeProvider);
            book.OrderBookEvent += (_, args) => args.Events.ToList().ForEach(x => Console.WriteLine(args));

            var preOpen = new TimeSpan(1, 0, 0);
            var open = new TimeSpan(1, 10, 0);
            var close = new TimeSpan(22, 10, 0);
            var sessionProvider = new SessionProvider(preOpen, open, close);
            sessionProvider.Changed += (_, args) => book.UpdateStatus(args.Status);
            sessionProvider.Update(new DateTime(2020, 1, 1, 1, 30, 0));

            book.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Buy, 100, 3);
            book.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Sell, 100, 5);
        }

        public static void BackTestExample()
        {
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);

            var timeProvider = new TestTimeProvider(DateTime.Now);
            IOrderBook book = new InMemoryOrderBook(sec, timeProvider);
            book.OrderBookEvent += (_, args) => args.Events.ToList().ForEach(x => Console.WriteLine(args));

            var preOpen = new TimeSpan(1, 0, 0);
            var open = new TimeSpan(1, 10, 0);
            var close = new TimeSpan(22, 10, 0);
            var sessionProvider = new SessionProvider(preOpen, open, close);
            sessionProvider.Changed += (_, args) =>
            {
                timeProvider.SetCurrentTime(args.Time);
                book.UpdateStatus(args.Status);
            };

            // loop through data
            for (var i = 0; i < 100; i++)
            {
                var time = new DateTime(2020, 1, 1, 1, 30, 0);

                // update status with correct time
                sessionProvider.Update(time);
                // set data time
                timeProvider.SetCurrentTime(time);
                // pass in data
                book.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Buy, 100, 3);
            }
        }

        public static void LiveExample()
        {
            var sec = new Security("GCZ6", SecurityType.Future, 10, 10);

            var timeProvider = new UtcTimeProvider();
            IOrderBook book = new InMemoryOrderBook(sec, timeProvider);
            book.OrderBookEvent += (_, args) => args.Events.ToList().ForEach(x => Console.WriteLine(args));

            var preOpen = new TimeSpan(1, 0, 0);
            var open = new TimeSpan(1, 10, 0);
            var close = new TimeSpan(22, 10, 0);
            var sessionProvider = new SessionProvider(preOpen, open, close);
            sessionProvider.Changed += (_, args) => book.UpdateStatus(args.Status);
            Task.Run(() =>
            {
                var i = 0;
                while (i < 100)
                {
                    // this needs to happen on same thread as book is updated   
                    sessionProvider.Update(timeProvider.GetCurrentTime());
                    Thread.Sleep(100);
                    i++;
                }
            });

            book.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Buy, 100, 3);
            book.CreateLimitOrder(Guid.NewGuid(), Guid.NewGuid(), OrderValidity.Day, Side.Sell, 100, 5);
        }
    }
}