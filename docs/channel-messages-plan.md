# The messages a channel does not publish yet

A plan, not yet started. Circus publishes five products - by-price, by-order, trades, status and
the indicative quote - and both venues it models publish more than that. This is what the rest
are, why each one cannot be derived by a subscriber from what is published today, and where each
belongs in a codebase whose organising rule is that everything a consumer knows comes out of the
event stream.

## What is missing

Held against CME's MDP 3.0 and Eurex's EMDI/EOBI/RDI, seven things. They are listed in the order
they should be built, which is roughly cheapest-first and strictly dependency-first.

**The print does not say who lifted it.** `TradePrinted` carries a price, a quantity and a trade
id. Every real feed carries the aggressor's side as well - CME's `AggressorSide`, Eurex's
`AggressorSide` on `ExecutionSummary` - and it is the field a subscriber wants most, because
without it buy volume and sell volume are not separable and a tape is just a sequence of prices.
The book knows it at match time: `FillOrderConfirmed.IsResting` says which side of the pair was
resting, privately, and nothing broadcasts it.

**There is no trade summary.** `TradeDataEvent`'s comment already calls itself "CME's
TradeSummary", and it is not one. Circus prints once per resting order consumed, so an aggressor
taking two orders at one price prints twice; CME sends one entry for that price carrying the
aggregate quantity and `NumberOfOrders = 2`. Both shapes are real - Eurex's EOBI is per-execution
the way Circus is - so this is a second product rather than a correction to the first.

**There are no session statistics.** Open, high, low, last, session volume: none of them are
published, none of them are on the snapshot, and a subscriber that joins at noon has no way to
recover them because they are a fold over a stream it did not hear. The README already lists this
as missing, under Sessions rather than under Market data, which is the wrong half - the
accumulation is a session concern but the gap is a feed one.

**There are no daily statistics.** Settlement price, open interest, cleared volume. These are not
derivable at all - they come from clearing, overnight, and no amount of watching the book produces
them. CME sends them as `MDIncrementalRefreshDailyStatistics`, and a simulator that wants to model
a limit-up day needs the settlement anyway, because that is what the daily limit is set against.

**The limit prices are never published.** `DailyPriceLimitRestriction` resolves an upper and lower
bound from the settlement, `VolatilityBandRestriction` resolves a width, and a subscriber can see
neither. It is worse than an omission: `LimitStateChanged` carries the price the market is stuck
at, and `InstrumentStatusDataProducer` reads `limit.Side` and drops `limit.Price` on the floor, so
a feed says the market is limit-up without saying where. CME publishes
`MDIncrementalRefreshLimitsBanding`; Eurex carries the same numbers in reference data.

**There is no instrument definition.** A subscriber joining a channel is told a symbol and nothing
else - not the tick size, not the matching algorithm, not the trading hours, not the price
restrictions in force. Everything downstream currently hardcodes it: `LiquidityAgentOptions` takes
a `ReferencePrice` because the agent has no way to ask. CME replays its whole definition set on
the incremental channel on a cycle; Eurex publishes RDI as a separate stream. Either way it is a
message, and here it would be the first one that describes the venue's configuration rather than
its activity.

**There is no end-of-event marker.** One dispatch can produce a status change, three by-price
deltas, two prints and an order update, and a subscriber receives seven messages with nothing
saying where the action's worth of them ends. Each message is individually coherent, which is what
the one-message-per-action rule buys; the group is not marked, which is what CME's
`MatchEventIndicator.EndOfEvent` is for and what Eurex's packet `CompletionIndicator` is for. Also
already on the README's list.

Two things deliberately stay out. **Implied prices** need spread instruments and a second book to
imply into, and there are none. **Heartbeats** answer "is this feed alive", which is a question
about a wire, and there is no wire here - a channel is a method call that returned nothing.

## Where each one goes

The codebase has one rule that decides most of this, and it is in `IIncrementalProducer`: a
producer is a pure function of the events it is handed, and derivation moves into the book exactly
when a producer would otherwise have to shadow the book or read a participant's private events.
The second rule follows from the first: anything a joining subscriber cannot rebuild has to reach
it on `BookSnapshot`, and `BookSnapshot` is something the book emits, so anything recoverable is
something the book holds.

Applying those two:

| Message | Where it is derived | Why |
| --- | --- | --- |
| Aggressor side | Book, as a field on `TradePrinted` | Only the book knows which side was resting, and the field that says so is private |
| Trade summary | Producer | Grouping one dispatch's prints by price is a pure function of them |
| Session statistics | Book | A joiner cannot fold a stream it did not hear, so they must be on the snapshot |
| Daily statistics | Action in, book holds, producer translates | Not derivable from anything; arrives from outside like `OpenTrading.ReferencePrice` does |
| Limits and banding | Book | The restrictions resolve the numbers and only the book holds the restrictions |
| Instrument definition | Book, on a new action | Keeps the definition reproducible by replay rather than injected beside it |
| End-of-event marker | Channel | It marks a dispatch boundary, and the channel is what knows one |

## Step 1 - the aggressor, and the trade summary

`TradePrinted` and `TradeDataEvent` each gain `Side AggressorSide`. The book sets it where it
already pairs the two fills. Not nullable: every trade has an aggressor, including an auction's,
where the incoming order that crossed the book is the aggressor for each print it caused.

One judgement call to make here rather than defer: an auction uncrossing at a single price has a
whole side of the book as its aggressor and no meaningful answer. CME reports auction trades with
`AggressorSide = NoAggressor`. So the field is `Side?` after all, null for a print struck by an
auction, and the comment says which case that is.

`FeedProducts` gains `TradeSummary`. `TradeSummaryProducer` folds one dispatch's `TradePrinted`
events by price, in the order the prices were touched:

```csharp
public record TradeSummaryEntry(decimal Price, int Quantity, int NumberOfOrders,
    Side? AggressorSide, IReadOnlyList<string> TradeIds);

public record TradeSummaryDataEvent(string Symbol, DateTime Time,
    IReadOnlyList<TradeSummaryEntry> Entries) : MarketDataEvent(Symbol, Time);
```

`NumberOfOrders` is the count of prints at that price, which is the count of resting orders
consumed there, which is what CME's field means. `TradeIds` is what joins the summary back to the
by-order feed's `Filled` changes - CME carries the same join as `NoOrderIDEntries` on channels
that also carry MBO - and it is what stops the summary from being a lossy version of the print
stream rather than a different view of it.

One message per dispatch carrying every entry, for the reason `MarketByPriceDeltaEvent` is one
message carrying every level: the set of trades one action made is a single fact about that
action, and the channel stamps one sequence for it.

Carrying a collection means spelling out `Equals`, `GetHashCode` and `ToString` by hand, the way
every other message carrying one does, because `DeterminismTests` compares replayed events by
value.

## Step 2 - session statistics

The book accumulates, because the snapshot has to carry them:

- opening price - the first print of the session, which is the auction's
- session high and low
- last traded price - already held as `_lastTradedPrice`, and never published
- session volume - the sum of printed quantities
- session high bid and low offer, which CME publishes and which the book sees for free

Reset on the phase that `StartsSession`, which pre-open already declares and which is exactly the
boundary that reseeds sequence numbers today. A pause does not reset them; a close does not clear
them, because a closed book's statistics are the day's answer and a subscriber joining after the
close should still get it.

The book emits `SessionStatisticsChanged` when any of them moves, carrying all of them - one
composite rather than one event per statistic, so a consumer never holds a partial picture, which
is the same reason `InstrumentStatusDataEvent` is a composite. Emitted at most once per dispatch,
after the prints that moved it.

`FeedProducts.Statistics`, `SessionStatisticsProducer` translating the event,
`SessionStatisticsSnapshotProducer` reading new fields on `BookSnapshot`.

## Step 3 - daily statistics

Settlement price, open interest and cleared volume arrive from outside as an action:

```csharp
public sealed record PublishDailyStatistics : OrderBookAction
{
    public decimal? SettlementPrice { get; init; }
    public int? OpenInterest { get; init; }
    public int? ClearedVolume { get; init; }
}
```

An action rather than a setter, for the reason `PublishSnapshot` is one: it goes through the
sequencer, lands at a defined point in the dispatch order, and a replay of the trace reproduces
the statistics message along with everything else. Nullable each, so a venue that knows its
settlement before it knows its open interest sends what it has.

The book holds them, publishes `DailyStatisticsChanged`, and puts them on `BookSnapshot`. They go
out under `FeedProducts.Statistics` alongside the session ones - CME splits them across two
message types and one channel, and splitting them into two products here would buy a venue shape
nobody runs.

**Decision to make before writing this.** The settlement price and the reference price that
`PreOpenTrading`/`OpenTrading` already carry are the same number: the daily limit is anchored on
the reference, and the reference is the previous day's settlement. Two ways in for one number is
how they drift apart. The proposal is that `PublishDailyStatistics.SettlementPrice` becomes the
one that anchors the limits, and the `ReferencePrice` on the two status actions stays as the
override it effectively is - but this should be settled deliberately, because it is the one part
of this plan that changes existing behaviour rather than adding to it.

## Step 4 - limits and banding

Two halves, and the smaller one first because it is closer to a bug than a feature:
`InstrumentStatusDataEvent` gains `decimal? LimitPrice`, and `InstrumentStatusDataProducer` stops
dropping the price off `LimitStateChanged`. `BookSnapshot` carries it too, since a joiner into a
limit-locked market is exactly who needs it.

The other half is the band itself. `IPriceRestriction` gains a way to report its resolved bounds -
an upper and lower tick, null until a reference resolves them, which `SessionLimitAnchor` already
computes and keeps to itself. The book publishes `PriceLimitsChanged(Symbol, Time, decimal?
UpperLimit, decimal? LowerLimit, decimal? MaxPriceVariation)` when a session change or a new
reference moves them, carries them on `BookSnapshot`, and `FeedProducts.Limits` decides who hears
it.

`MaxPriceVariation` is the volatility band's width, which is CME's name for it. A restriction that
has no bounds to report - an order price band, which is relative to the market rather than to a
reference - reports none, and a book with no such restriction publishes no message at all.

## Step 5 - instrument definitions

The largest step, and the one with a design decision in it.

A definition is not derived from a book's activity, so the question is how it reaches the feed
without breaking the rule that a subscriber's whole world comes from the event stream. Handing
`InstrumentGroup` the `Instrument` and letting `InstrumentFeed` publish it directly would be
simpler and is wrong: the message would not be reproduced by a replay of the action stream, which
is the property `PublishSnapshot` was made an action to preserve.

So: a `PublishDefinition` action, a `DispatchKind.DefinitionTick` ordered with the snapshot tick,
and a `definitionInterval` on the `Sequencer` beside `snapshotInterval`. The book answers with
`InstrumentDefined` carrying what it knows - symbol, tick size, matching algorithm, market order
protection ticks, the configured price restrictions, its published depths, its current status.

What the book does not know is the schedule, which lives in the sequencer. Rather than teach the
book about schedules, `PublishDefinition` carries the sessions and the book echoes them - the same
shape as `OpenTrading.ReferencePrice`, where a number decided outside the engine reaches the book
as a field on an action. The sequencer fills it in from the `MarketSchedule` it already holds
beside the book.

`FeedProducts.Definitions`, an `InstrumentDefinitionProducer`, and no snapshot counterpart: the
definition cycle *is* the recovery mechanism, which is how CME does it - definitions are replayed
on the incremental feed rather than mirrored onto the snapshot one.

Two follow-ons worth naming and not doing here. `MarketView` should learn tick size from the
definition instead of the agent being told it, and `LiquidityAgentOptions.ReferencePrice` should
come from the daily statistics. Both are the payoff for this step, and both are cleaner as
separate changes.

## Step 6 - end-of-event markers

`ChannelMessage` gains `bool LastInEvent`, set on the final message a dispatch produces on that
channel. A flag rather than a message type: CME's `MatchEventIndicator` is a field on the message
for the same reason, and a marker message would need a sequence number of its own to say nothing.

Per channel, per stream. A dispatch that produces four messages on one channel and one on another
marks the fourth and the first respectively - the marker means "this channel has told you
everything about that action", which is the only thing a subscriber can act on. The snapshot
stream marks its own last message separately, since it is numbered separately.

CME's finer indicators - `LastTradeMsg`, `LastQuoteMsg`, `LastStatsMsg` - are not worth carrying:
a subscriber derives each of them from the message types it just received, and they exist on the
wire because a decoder there cannot look ahead.

## Step 7 - channel reset

`MarketDataChannel` numbers from one and never resets, so a subscriber counting messages across a
trading day boundary sees a sequence that has run all week. Real channels reset: CME sends a
Channel Reset and restarts `MsgSeqNum` at 1.

`ChannelReset` as a message on both streams, both sequences back to zero, on the dispatch that
ends the trading day - which the book already distinguishes, since `CloseTrading.EndsTradingDay`
is the flag that decides whether Day orders retire. A subscriber's rule becomes: a gap is loss,
unless a reset said otherwise.

Small, but it has to come after step 6 rather than before, because a reset is the one message that
is its own event and the marker rules need to already exist for it to sit inside them.

## What the tests look like

The shape is already there and each step slots into it.

Producer tests per new producer, in `tests/Circus.Tests/MarketData`, mirroring
`TradeDataProducerTests` and `InstrumentStatusDataProducerTests`.

`SnapshotRecoveryTests` gains the case that matters most: a subscriber joining after a session's
first hour, applying a snapshot, and holding the same statistics and limits as one that heard
every message. That test is the reason statistics live in the book rather than in a producer, so
it is the one that would catch the design being got wrong.

`VenueSession`'s trace needs two things it does not have: a close, so statistics have a session
boundary to reset on, and a `PublishDailyStatistics` before the open, so the limits have something
to resolve against. Adding them changes every existing assertion's message counts, so it should
happen once, at step 2, rather than drifting in over several steps.

`CmeShapedVenueTests` and `EurexShapedVenueTests` then say what the two shapes actually differ
over, which is more than they can say today:

```csharp
group.AddChannel("310",
    FeedProducts.ByPrice | FeedProducts.ByOrder | FeedProducts.TradeSummary |
    FeedProducts.Status | FeedProducts.Statistics | FeedProducts.Limits |
    FeedProducts.Definitions | FeedProducts.Indicative,
    depth: 10);

group.AddChannel("EOBI",
    FeedProducts.ByOrder | FeedProducts.Trades | FeedProducts.Status, snapshotEvery: 6);
group.AddChannel("EMDI",
    FeedProducts.ByPrice | FeedProducts.Trades | FeedProducts.Status |
    FeedProducts.Statistics | FeedProducts.Indicative, depth: 10);
group.AddChannel("RDI", FeedProducts.Definitions | FeedProducts.Limits);
```

That is the point of the whole exercise. CME sends an aggregated trade summary and Eurex sends
per-execution prints; CME carries definitions on the same channel as the book and Eurex carries
them on a separate one. Both are a set of flags here, and neither is a code path.

`FeedProducts.All` grows to include the new flags, which keeps a caller who has not thought about
channels seeing the whole venue.

`DeterminismTests` needs nothing new beyond the hand-written equality on the messages carrying
collections, which it will catch immediately if forgotten.

## Ordering inside a feed

`InstrumentFeed.Process` publishes by producer in a fixed order, because every event in one
dispatch shares an instant and there is no time order among them to preserve. The order becomes:

    Definition, Status, Limits, Trades, TradeSummary, ByPrice, ByOrder, Indicative, Statistics

Definitions first because they describe what everything after them is about; status and limits
next because they say whether the book was allowed to do what follows; trades before depth
because a print is the cause and a level change is the consequence; statistics last because they
are a consequence of the print. That is CME's ordering within a match event, and it is the one a
subscriber rendering a tape wants.

## README

Six checkboxes move when this is done. Under Market data: end-of-event markers, instrument
definition messages, trade summaries, session and daily statistics, limits and banding. Under
Sessions: market statistics, which should move to Market data rather than being ticked where it
is.
