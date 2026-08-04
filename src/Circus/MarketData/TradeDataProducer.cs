using Circus.Events;

namespace Circus.MarketData;

// The public print, derived from the private fills. A trade produces two FillOrderConfirmed
// events sharing a TradeId, and a venue broadcasts one message for the pair - so this emits on
// the first fill of each trade and skips the second.
//
// Keyed on the id changing rather than on IsResting, which today would pick out the same event:
// one print per distinct trade is what is meant, and it stays true if a trade ever involves more
// than the two sides it does now. The two fills of a trade are emitted adjacent, so remembering
// the last id seen is enough and no set is needed.
//
// The last id is kept across calls, not just within one. A trade's fills always arrive in the
// same batch, so this only matters for the pathological case of a trade id repeating across
// consecutive batches, which the book's forward-only numbering already rules out.
public class TradeDataProducer : IIncrementalProducer<TradeDataEvent>
{
    private string? _lastTradeId;

    public IList<TradeDataEvent> Process(IReadOnlyList<OrderBookEvent> events)
    {
        List<TradeDataEvent>? output = null;

        foreach (var ev in events)
        {
            if (ev is not FillOrderConfirmed fill || fill.TradeId == _lastTradeId)
                continue;

            _lastTradeId = fill.TradeId;
            output ??= new List<TradeDataEvent>();
            output.Add(new TradeDataEvent(fill.Symbol, fill.Time, fill.Price, fill.Quantity));
        }

        return output ?? (IList<TradeDataEvent>) Array.Empty<TradeDataEvent>();
    }
}
