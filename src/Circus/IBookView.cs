using Circus.MarketData;

namespace Circus;

// The book answering questions about what it is holding right now, rather than saying what just
// changed. Deliberately a seam of its own and not part of IOrderBook, whose whole contract is
// that a consumer derives its view from the event stream and never queries the book back.
//
// That contract is right for an incremental feed and wrong for a snapshot. A snapshot is a
// statement of current state, published so a subscriber joining mid-session - or recovering from
// a detected gap - can start from something true rather than replay a session's history it never
// saw. Deriving one from events would mean keeping the very book the subscriber is missing, so
// the only honest source is the book itself. CME and Eurex draw the line in the same place: the
// incremental feed carries changes, a separate snapshot feed carries state.
//
// Implemented by OrderBook alone. A wrapper in front of a book (TimestampingOrderBook) forwards
// it only if whatever it wraps can answer, since nothing here is derivable from the wrapping.
public interface IBookView
{
    string Symbol { get; }

    OrderBookStatus Status { get; }

    // Aggregated working-book depth, best first, capped at maxLevels occupied levels per side.
    // Quantities are displayed size: an iceberg's hidden reserve is not on the book and must not
    // reach a public feed. Untriggered stops rest in a separate ladder and never appear here.
    IReadOnlyList<Level> GetLevels(Side side, int maxLevels);
}
