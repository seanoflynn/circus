using Circus.Actions;
using Circus.Events;
using Circus.MarketData;

namespace Circus.Agents;

// A participant at the venue: something that watches the market, watches its own orders, and
// sends actions.
//
// It knows the market because it subscribed to the feed, and it knows what it is holding because
// it saw its own confirms and fills. Nothing here reaches into a book, and nothing here keeps a
// book of its own - which is the whole point. A participant that had to run its own matching
// engine to know what it was holding would be modelling the venue rather than trading at it, and
// would drift from it the moment the two disagreed.
//
// Observing and acting are separate calls rather than one OnEvent that may return actions. A
// whole dispatch's worth of events reaches every agent before any of them acts, so what an agent
// decides is a function of where the venue got to by the tick boundary rather than of how many
// events happened to arrive in one batch. That is what keeps a run reproducible.
//
// Single-threaded, like everything it plugs into: an agent is called from the same thread that
// ticks the venue.
public interface IAgent
{
    // Who this agent trades as. It is what its own events are routed by, and what the book keys
    // its orders under along with the client order id - so two agents sharing one company id are
    // one firm with two desks, and each sees the other's fills the way a firm's drop copy does.
    string CompanyId { get; }

    // The instruments it wants the feed for. An agent is not required to trade all of them, and
    // is not stopped from sending actions for one it did not subscribe to - though it would then
    // be trading blind, which is a mistake worth making obvious rather than preventing.
    IReadOnlyList<string> Symbols { get; }

    // The public feed, for the instruments above. The same messages, in the same order, that any
    // other subscriber to that channel receives.
    void OnMarketData(MarketDataEvent data);

    // Its own order events - the ones carrying its company id, and only those. Rejections arrive
    // here too: an agent that is told nothing about an action the venue refused would go on
    // believing in an order that does not exist.
    void OnOwnEvent(OrderBookEvent ev);

    // What it wants to send, given where the venue had got to when this tick began. The actions
    // come back unstamped: a participant does not get to say when its order reached the exchange,
    // so the venue stamps them on the way in.
    IReadOnlyList<OrderBookAction> Act(DateTime now);
}
