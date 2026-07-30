namespace Circus.MarketData;

// What a subscriber receives, and the one type a feed carrying several instruments can be a
// stream of.
//
// Security travels on every message because a feed is not per instrument. A subscriber filtering
// to one contract, or keying its own mirrored book off it, has to be able to tell them apart from
// the message rather than from which stream it arrived on - the same reason a stock locate code
// is on every ITCH message and a SecurityID on every CME one.
//
// Mirrors OrderBookEvent, which carries the same pair for the same reason. A wire protocol would
// replace Security here with a compact numeric id resolved once per session, but that is a
// serialization concern and belongs at whatever boundary does the encoding rather than in the
// events themselves - in process this is a reference, not a repeated payload.
//
// Keyed on Security.Name where a consumer needs a dictionary: two Security records describing the
// same contract need not be equal, since the restriction list on them compares by reference.
public abstract record MarketDataEvent(Security Security, DateTime Time);
