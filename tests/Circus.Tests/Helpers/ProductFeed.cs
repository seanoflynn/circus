using Circus.Events;
using Circus.MarketData;

namespace Circus.Tests.Helpers;

// A feed carrying one product, and that product's messages out of it.
//
// Replaces ProducerInput, which filtered a book's events down to the half a venue broadcasts so a
// test could hand them to a producer directly. That filter was a copy of the one InstrumentFeed
// does at its own boundary, which meant the guarantee it exists for - that a participant's own
// confirmations cannot reach a public feed - was exercised by a reimplementation of itself and
// never by the production code. Driving the feed tests the real one.
//
// A feed carrying a single product publishes only that product, so the cast below cannot fail; a
// test wanting several asks the feed for them without this.
internal static class ProductFeed
{
    public static InstrumentFeed Carrying(FeedProducts product, string symbol = "GCZ6") =>
        new(symbol, product);

    public static IList<T> Publish<T>(this InstrumentFeed feed, IReadOnlyList<OrderBookEvent> bookEvents)
        where T : MarketDataEvent =>
        feed.Process(bookEvents).Cast<T>().ToList();

    // The snapshot stream, which is a separate call because it is a separate stream - and which
    // counts a tick, so a test asserting a cadence holds one feed across dispatches.
    //
    // Named for the image rather than the stream so that it does not read as the PublishSnapshot
    // action, which is the thing a caller passes the book to get one of these out.
    public static IList<T> PublishImage<T>(this InstrumentFeed feed, IReadOnlyList<OrderBookEvent> bookEvents)
        where T : MarketDataEvent =>
        feed.Snapshot(bookEvents).Cast<T>().ToList();
}
