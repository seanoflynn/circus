# Sessions that span midnight

A plan. Nothing here has been built.

Overnight sessions are not supported today, and the code says so where it decides: `TradingSession`
holds three times of day, all of which must fall within the same day. A product that opens at 17:00
and closes at 16:00 the next afternoon - which is most of what CME lists, and the shape Globex has
had since it was Globex - cannot be described to this venue at all.

## Where we are

`TradingSession(TimeSpan PreOpen, TimeSpan Open, TimeSpan Close)` is a triple of times of day, and
three things hold it to one calendar day:

- `MarketSchedule`'s constructor requires `PreOpen <= Open <= Close` within each session, and each
  session's `PreOpen` to be at or after the previous one's `Close`. An overnight session written as
  `(17:00, 17:00, 16:00)` is rejected by the second of those - `Open > Close`.
- `MarketSchedule.SessionAt` asks whether a `TimeOfDay` falls in `[PreOpen, Close)`, so a session in
  progress can only ever be one that began earlier on the same date. At 02:00 nothing is running,
  whatever opened last evening.
- `MarketSchedule.NextAfter` anchors every boundary it returns on `time.Date`, so no transition it
  produces can land on a different date to the instant it was asked about.

Everything downstream is already indifferent to this and stays that way. The `Sequencer` holds one
pending transition per book, gets it from `NextAfter`, and queues the next only once the current one
has been dispatched - it never reasons about dates. `InstrumentGroup` passes a schedule through.
`OrderBook` reads `EndsTradingDay` off the close it is handed and never asks what day it is except
in the two places listed under step 4.

So the change is contained: it is a change to what a schedule can describe and to how it answers,
plus one decision about what "the trading day" names once a session outlives a date.

## Where we are going

A session's boundaries become offsets from the start of the day it is anchored on, rather than times
within that day. `TimeSpan` already carries this: `new TradingSession(new TimeSpan(17, 0, 0), new
TimeSpan(17, 0, 0), new TimeSpan(40, 0, 0))` is Globex's day - pre-open and open at 17:00, close at
16:00 the following afternoon. No new type, no `DayOffset` field beside each time, and a schedule
that does not span midnight is written exactly as it is written today.

The schedule stays what it is: stateless, one day repeated, answering "what is due after this
instant" rather than walking. That property is what the `Sequencer` needs and it survives the change
intact - the only thing that grows is how far back the query has to look for a session that might
still be running.

The alternative shapes were considered and are not recommended. An explicit `DayOffset` per boundary
says the same thing in three more fields and invites the two of them to disagree. A calendar of
concretely dated sessions - which is what holidays and half-days will eventually want - is a much
larger change that this one does not block: a dated calendar is a different implementation of the
same `NextAfter` question, and building it later is easier against a `TradingSession` that already
knows a session can outlive a date.

## Steps

### 1. Let a session's offsets exceed a day

`TradingSession`'s comment is the specification and it is what changes first. The three times become
offsets from the anchor day's midnight, `Close` may exceed 24 hours, and the record itself is
otherwise untouched.

`MarketSchedule`'s per-session check (`PreOpen <= Open <= Close`) already reads correctly against
offsets and needs nothing. The ordering check between neighbours does too.

What is new is a bound on the whole schedule. A day repeated indefinitely only makes sense if one
day's sessions cannot reach into the day after next:

```csharp
if (sessions[^1].Close - sessions[0].PreOpen >= TimeSpan.FromDays(1))
    throw new ArgumentException("a day's sessions must span less than 24 hours");
```

Strictly less rather than at most, which is what step 2 needs and what step 3 explains. This single
check does two jobs: it keeps a schedule a description of one repeating day, and it caps how far
back a query has to look at exactly one day.

### 2. Ask the question against dates rather than times of day

`NextAfter` currently compares `time.TimeOfDay` against offsets and anchors its answer on
`time.Date`. Both become comparisons of `DateTime` against `anchor.Add(offset)`, where the anchor is
a candidate date rather than always today.

Finding the session in progress becomes a scan over two anchors:

```csharp
// Yesterday first: a session that began before midnight may still be running. Nothing earlier
// can reach here, the constructor having capped a day's whole span below 24 hours.
for (var dayOffset = -1; dayOffset <= 0; dayOffset++)
{
    var anchor = time.Date.AddDays(dayOffset);
    if (SessionAt(anchor, time) is not { } index) continue;

    var session = _sessions[index];
    return time < anchor.Add(session.Open)
        ? new ScheduledTransition(anchor.Add(session.Open), OrderBookStatus.Open)
        : new ScheduledTransition(anchor.Add(session.Close), OrderBookStatus.Closed,
            EndsTradingDay(index));
}
```

Yesterday is tried first because that is where a session in progress can only be: today's sessions
have not started yet if yesterday's is still running, non-overlap being what the constructor checks.

The out-of-session branch is the same shape - the next pre-open at or after `time`, trying today's
sessions in order and falling through to tomorrow's first. `SessionAt` keeps its half-open interval
(`>= PreOpen`, `< Close`), which is what continues to let a close and the next pre-open coincide,
and which step 3 is about.

`NextSessionAt` and its `(Index, DayOffset)` return exist for a stateful walker that anchors
boundaries on its caller's date. Nothing implements that walker - the comment describes a consumer
that was never written - so both it and `Sessions` should go rather than be carried through this
change and left wrong. `EndsTradingDay` stays; it is asked by `NextAfter` itself.

### 3. Decide what a shared boundary means

`MarketSchedule` currently permits one session's `PreOpen` to equal the previous session's `Close`,
and then cannot reach both: standing on the close, the next question steps over the pre-open and
lands on the boundary after it, so a session opens without ever pre-opening. The comment on
`NextAfter` says as much and leaves the question open.

Overnight schedules make it reachable rather than hypothetical, because they are how a nearly
continuous venue is written and a nearly continuous venue is exactly one whose sessions touch. The
recommendation is to close it by construction: require a strictly positive gap between one session's
close and the next's pre-open, and between the day's last close and tomorrow's first pre-open (which
step 1's strict inequality already gives).

That costs nothing real. Venues that run almost around the clock still stop - CME's maintenance
window is an hour, Eurex's is minutes - and a venue that genuinely never closes is one session with
no boundary rather than two that touch. It also turns a latent wrong answer into an exception at the
point the schedule is written, which is where it can be understood.

The alternative is a query that carries the status the caller last applied, so it can distinguish
standing on a close from standing on a pre-open. That makes `NextAfter` stateful in its arguments to
serve a case with no venue behind it, and is not recommended.

### 4. Say which day a session belongs to

This is the one behavioural decision in the plan, and it is the reason the change is not just
arithmetic in `MarketSchedule`.

A session that opens on Sunday evening and closes on Monday afternoon is Monday's trading day
throughout - a trade printed at 22:00 on Sunday is a Monday trade. Nothing in the engine can know
that today, because the two places that need a date read it off the wall clock:

- `OrderBook.CreateOrder` rejects a GTD order whose date is before `DateOnly.FromDateTime(time)`.
- `ExpireOrders` retires GTD orders whose date is at or before `DateOnly.FromDateTime(time)`.

Under a same-day schedule those agree with the trading day. Under an overnight one they do not: an
order sent at 22:00 on Sunday good till Monday is, by the wall clock, good till tomorrow - and it
would then survive the Monday-afternoon close it was meant to die at, because that close falls on
Monday and the order is good till Monday. Off by one session, in the direction that leaves an order
resting that a participant believes has expired.

So the trading day becomes something the schedule states rather than something the book infers:

- `TradingSession` gains `int TradeDateOffset = 0` - how many days past its anchor date the session's
  trading day falls. Globex's Sunday evening session carries 1; a lunch-broken cash session carries
  0, which is the default and which is why no existing schedule changes.
- `ScheduledTransition` gains `DateOnly TradeDate`, computed as `anchor.AddDays(session.TradeDateOffset)`.
- The `PreOpenTrading`, `OpenTrading` and `CloseTrading` actions gain an optional `TradeDate`. The
  `Sequencer` fills it from the transition; a caller driving a book by hand leaves it null.
- `OrderBook` keeps the current trading day in a field, set on a phase whose `StartsSession` is true
  and defaulting to the instant's own date when the action does not say - which is exactly today's
  behaviour, so a book driven directly through `OrderBookExtensions` is unaffected. The two reads
  above consult that field instead of the clock.

The sequence and trade id seed in `UpdateStatus` should come from the same field. It is derived from
the transition instant today, which for an overnight session means ids carrying the date the session
started rather than the day it belongs to. The `Math.Max` that keeps the seed forward-only means
this is not a correctness problem either way, but an id whose date is the trading day is the one
worth having, and once the field exists it is free.

### 5. Tests

`MarketScheduleTests` is where most of this is provable, and it should get an overnight schedule
beside the two it already has - `(17:00, 17:00, 40:00)`, a day with nothing else in it:

- Asked before the pre-open, the pre-open today.
- Asked during the evening, the close - tomorrow's date, this session's.
- Asked after midnight, still that same close, anchored on yesterday. This is the case that fails
  outright today and is the point of the change.
- Asked between the close and the next pre-open, that pre-open, today.
- A schedule spanning 24 hours or more, rejected.
- Touching boundaries, rejected (step 3).

Then a schedule with an evening session and a next-day cash session, to prove `EndsTradingDay` still
names the day's last close when that close is on a different date to the pre-open that began the
day, and that the two sessions do not overlap across the wrap.

Beyond the schedule, one venue-level test in `tests/Circus.Tests/Sessions`: run a book through an
overnight session with a Day order and a GTD order resting, and prove both survive midnight and
retire at the session's close rather than at the date change. That is the assertion a participant
would actually make, and it is the one step 4 exists for.

`Circus.Tests.Venues` runs one trace through several venue shapes and is where a schedule that
crosses midnight belongs next, once the above lands - the trace is stamped within a day today and
would need its own instants moved, so it is a follow-on rather than part of this.

### 6. README

`Overnight sessions (spanning midnight)` sits under Sessions, unchecked until the above lands.

## What this does not do

Time zones and daylight saving. A schedule is offsets against whatever `DateTime` it is given, and
the venue supplies UTC. A 17:00 Chicago boundary is 22:00 UTC for part of the year and 23:00 for the
rest, and nothing here shifts with it. That is already true of same-day schedules and is not made
worse by this change, but overnight sessions are where it starts to matter - a product that trades
almost around the clock has its boundaries in one particular local evening, and an hour's drift in
them is an hour of the wrong status. A schedule that carries a `TimeZoneInfo` and resolves its
offsets against the anchor date's local midnight is the shape that fixes it, and it composes with
everything above: the anchor date is already the unit this plan works in.

Holidays and half-days, which remain what `NextAfter`'s nullable return is written for and which
nothing here brings closer or pushes further away.
