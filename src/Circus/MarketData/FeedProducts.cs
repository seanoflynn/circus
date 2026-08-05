namespace Circus.MarketData;

// Which products a feed carries, and so what a channel built on it publishes.
//
// One flag per product rather than per message, because a product's incremental and snapshot
// halves are the same thing seen twice - a channel carrying market by price carries both the
// deltas and the images. Whether snapshots are published at all is a separate question, answered
// by whether the venue runs a snapshot cycle.
//
// This is what lets one engine wear different venues. CME channels carry by-price and by-order
// together with trades and status; Eurex splits by-order onto EOBI and by-price onto EMDI, with
// state on both; an ITCH-shaped venue publishes by-order alone and leaves aggregation to its
// subscribers. Those are three sets of these flags rather than three implementations.
[Flags]
public enum FeedProducts
{
    None = 0,

    // Aggregated depth - CME's Market by Price, Eurex's EMDI.
    ByPrice = 1,

    // Order by order - CME's Market by Order, Eurex's EOBI.
    ByOrder = 2,

    // The public print, one per trade.
    Trades = 4,

    // What state the instrument is in, as one composite.
    Status = 8,

    // The auction quote a book is running.
    Indicative = 16,

    // Everything, which is more than a real depth feed carries and the useful default for a
    // simulator: a caller who has not thought about channels should still see the whole venue.
    All = ByPrice | ByOrder | Trades | Status | Indicative
}
