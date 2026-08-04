using Circus.Events;

namespace Circus.Tests.Helpers;

internal static class OrderFlowEvents
{
    // Everything an action produced except the aggregated-depth reports.
    //
    // A book emits two different kinds of thing: what happened to orders, and what that left the
    // published book looking like. Most tests are about the first - "this order rested, that one
    // filled, this one was rejected" - and counting the raw list makes those assertions depend on
    // how many price levels happened to move, which is a different subject and one the by-price
    // tests already cover.
    //
    // So an order-flow test asserts against this rather than the whole list, the same way it
    // asserts trades through Trades() rather than by grouping fills at every site. A test that is
    // about the depth reports reads them directly.
    public static IReadOnlyList<OrderBookEvent> OrderFlow(this IEnumerable<OrderBookEvent> events) =>
        events.Where(e => e is not LevelChanged).ToList();
}
