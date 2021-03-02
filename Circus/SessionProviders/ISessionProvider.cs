using System;
using Circus.OrderBook;

namespace Circus.SessionProviders
{
    public interface ISessionProvider
    {
        event EventHandler<SessionStatusChangedArgs> Changed;

        void Update(DateTime current);
    }

    public record SessionStatusChangedArgs(OrderBookStatus Status, DateTime Time);
}