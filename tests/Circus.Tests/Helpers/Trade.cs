using Circus.Events;

namespace Circus.Tests.Helpers;

// A trade as a consumer reconstructs it: the pair of FillOrderConfirmed events sharing a
// TradeId, resting side first.
//
// The book emits fills rather than trades, because a fill belongs to one participant and a
// trade does not - which is why there is no longer an event carrying both sides. A test
// asserting "one trade printed at 100 for 5" is doing what TradeDataProducer does, and does it
// through this rather than through the same grouping written out at every site.
//
// FlatteningTests covers the shape this hides - two top-level fills, one shared id - so that
// nothing here can quietly go back to assuming a wrapper.
internal sealed record Trade(string TradeId, string Symbol, DateTime Time, decimal Price, int Quantity,
    IReadOnlyList<FillOrderConfirmed> Fills)
{
    public FillOrderConfirmed Resting => Fills[0];
    public FillOrderConfirmed Aggressor => Fills[1];
}

internal static class TradeEvents
{
    // Grouped by TradeId, in the order the trades first print. The two fills of a trade are
    // emitted adjacent and resting-first, and are kept in that order.
    public static List<Trade> Trades(this IEnumerable<OrderBookEvent> events)
    {
        var byId = new Dictionary<string, List<FillOrderConfirmed>>();
        var order = new List<string>();

        foreach (var fill in events.OfType<FillOrderConfirmed>())
        {
            if (!byId.TryGetValue(fill.TradeId, out var fills))
            {
                byId[fill.TradeId] = fills = new List<FillOrderConfirmed>();
                order.Add(fill.TradeId);
            }

            fills.Add(fill);
        }

        return order.Select(id =>
        {
            var fills = byId[id];
            return new Trade(id, fills[0].Symbol, fills[0].Time, fills[0].Price, fills[0].Quantity, fills);
        }).ToList();
    }
}
