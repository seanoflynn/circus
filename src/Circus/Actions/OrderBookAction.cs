namespace Circus.Actions;

public abstract record OrderBookAction
{
    public required string Symbol { get; init; }

    // When the exchange accepted this action, stamped on the way in by whatever owns the clock
    // - a gateway, a session driver, TimestampingOrderBook. The book reads no clock of its own,
    // so this is the only time it knows: every event one action produces carries this instant,
    // and a book fed the same actions twice behaves identically both times. That is what makes
    // the action stream a complete record, replayable without a recorded clock beside it.
    //
    // Not the time the client sent it. An action is the book's input language, not a wire
    // protocol, and a participant does not get to say when their order arrived.
    public DateTime Time { get; init; }
}

public sealed record PreOpenTrading : OrderBookAction
{
    public decimal? ReferencePrice { get; init; }
}

public sealed record OpenTrading : OrderBookAction
{
    public decimal? ReferencePrice { get; init; }
}

// A trading day can hold several sessions, and only the last close of the day retires that
// day's Day/GoodTilDate orders - an intra-day close (a lunch break, say) leaves them resting.
// Defaults to true so a single-session day needs to say nothing.
public sealed record CloseTrading : OrderBookAction
{
    public bool EndsTradingDay { get; init; } = true;
}

// Interrupt trading within a session, keeping a quote. The book raises this itself on a
// volatility band breach; as an action it is the operator-driven equivalent.
public sealed record PauseTrading : OrderBookAction;

// Suspend trading with no price discovery. The book raises this itself when a restriction
// breach calls for a halt; as an action it is the operator-driven equivalent.
public sealed record HaltTrading : OrderBookAction;

// Nothing to report but the time itself. A timed interruption ends on its own, and a book with
// no order flow to carry it there needs something to ask - so a caller driving the book off a
// clock sends this as it ticks. Its whole payload is the Time every action carries, which is
// why it declares no members of its own.
public sealed record AdvanceTime : OrderBookAction;

// Report the current state of the book, as a snapshot feed does on its cycle. Changes nothing:
// the book answers with a BookSnapshot describing where it already is, which is the one thing a
// subscriber joining mid-session cannot derive from a stream it did not hear the start of.
//
// An action rather than a query, for the reason everything else here is one - it goes through the
// sequencer like any other, so a snapshot lands at a defined point in the dispatch order and a
// replay of the action stream reproduces the snapshot feed along with everything else. Carries
// nothing but the Time every action carries.
public sealed record PublishSnapshot : OrderBookAction;

public abstract record OrderAction : OrderBookAction
{
    public required string CompanyId { get; init; }
    public required string ClientOrderId { get; init; }
}

public abstract record CreateOrder : OrderAction
{
    public required OrderValidity OrderValidity { get; init; }
    public required Side Side { get; init; }
    public required int Quantity { get; init; }
    public SelfMatchPrevention? SelfMatchPrevention { get; init; }

    // Iceberg/display quantity - the portion shown to the market at a time, with the rest
    // held in reserve. Not restricted to CreateLimitOrder: Market/MarketLimit/triggered-Stop
    // orders can all end up resting as a plain limit order too, so display quantity is
    // meaningful for any of them.
    public int? MaxVisibleQuantity { get; init; }
}

public sealed record CreateLimitOrder : CreateOrder
{
    public required decimal Price { get; init; }
}

public sealed record CreateMarketOrder : CreateOrder;

public sealed record CreateMarketLimitOrder : CreateOrder;

public sealed record CreateStopLimitOrder : CreateOrder
{
    public required decimal Price { get; init; }
    public required decimal TriggerPrice { get; init; }
}

public sealed record CreateStopMarketOrder : CreateOrder
{
    public required decimal TriggerPrice { get; init; }
}

public sealed record UpdateOrder : OrderAction
{
    public required string PreviousClientOrderId { get; init; }
    public int? NewTotalQuantity { get; init; }
    public decimal? Price { get; init; }
    public decimal? TriggerPrice { get; init; }
}

public sealed record CancelOrder : OrderAction
{
    public required string PreviousClientOrderId { get; init; }
}