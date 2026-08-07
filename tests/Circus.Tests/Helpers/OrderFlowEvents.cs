using Circus.Events;

namespace Circus.Tests.Helpers;

internal static class OrderFlowEvents
{
    // Everything an action produced except the book's own reports of what it left behind.
    //
    // A book emits two different kinds of thing: what happened to orders, and what that did to the
    // displayed book. Most tests are about the first - "this order rested, that one filled, this
    // one was rejected" - and counting the raw list makes those assertions depend on how many
    // price levels moved or how the queue was rearranged, which is a different subject and one the
    // by-price and by-order tests already cover.
    //
    // Status, limit and indicative changes are not filtered: those are things that happened, not
    // reports of where the action left the book, and a session test counting them is asking a fair
    // question about order flow.
    //
    // So an order-flow test asserts against this rather than the whole list, the same way it
    // asserts trades through Trades() rather than by grouping fills at every site.
    public static IReadOnlyList<OrderBookEvent> OrderFlow(this IEnumerable<OrderBookEvent> events) =>
        events.Where(e => e is not (LevelsChanged or OrdersChanged or TradePrinted or BookSnapshot))
            .ToList();

    // The half of a book's output a venue may broadcast, for a test that wants to assert on it
    // directly rather than on the messages a feed makes of it.
    public static IReadOnlyList<MarketEvent> Market(this IEnumerable<OrderBookEvent> events) =>
        events.OfType<MarketEvent>().ToList();
}
