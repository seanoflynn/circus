using Circus.Events;

namespace Circus.MarketData;

// The public print, and nothing more than a translation of the one the book publishes.
//
// It used to derive the print itself, pairing the two FillOrderConfirmed events of a trade by the
// id they share and taking the first of each pair. That worked, but a fill belongs to the
// participant whose order filled and carries their CompanyId - so deriving a broadcast message
// from it meant a producer reading something no subscriber is entitled to. The book publishes
// TradePrinted for the same pair now, and the pairing lives where the trade happened.
//
// The trade's id comes across with it. It is the only field of a fill that is not about who filled
// - the two sides of a trade share it, and so do the order events the by-order feed publishes for
// them - so carrying it broadcasts nothing private and is what lets a subscriber holding both
// products join a print to the fills that made it.
public class TradeDataProducer : IIncrementalProducer<TradeDataEvent>
{
    public IList<TradeDataEvent> Process(IReadOnlyList<MarketEvent> events)
    {
        List<TradeDataEvent>? output = null;

        foreach (var ev in events)
        {
            if (ev is not TradePrinted trade)
                continue;

            output ??= new List<TradeDataEvent>();
            output.Add(new TradeDataEvent(trade.Symbol, trade.Time, trade.TradeId, trade.Price,
                trade.Quantity));
        }

        return output ?? (IList<TradeDataEvent>) Array.Empty<TradeDataEvent>();
    }
}
