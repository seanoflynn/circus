using Circus.Actions;
using Circus.Events;
using Circus.MarketData;

namespace Circus.Agents;

public interface IAgent
{
    string CompanyId { get; }

    IReadOnlyList<string> Symbols { get; }

    void OnMarketData(MarketDataEvent data);

    void OnOwnEvent(OrderBookEvent ev);

    IReadOnlyList<OrderBookAction> Act(DateTime now);
}
