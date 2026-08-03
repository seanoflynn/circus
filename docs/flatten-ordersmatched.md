# Flattening OrdersMatched

Prerequisite to phase 3 (`phase-3-fake-venue.md`), and worth doing before anything is built on
the current shape rather than after.

`OrdersMatched` goes. A trade becomes two `FillOrderConfirmed` events at the top level, each
carrying the id of the trade that produced them.

## Why

A participant must see its own fills and not its counterparty's. `OrdersMatched` makes that
impossible to express: it carries no `CompanyId` of its own and holds a fill for both sides, so
filtering on company either drops a participant's fills entirely or hands them the other side's
order. Every other event in the system is already scoped to one participant - `OrderEvent`
carries `CompanyId`, `ClientOrderId` and `ExchangeOrderId`, and `FillOrderConfirmed` inherits all
three. `OrdersMatched` is the single exception, and it exists only as a container.

Flattening it also removes a wart the determinism tests currently work around:

```csharp
// Rendered rather than compared directly: OrdersMatched carries its fills in an IList, and a
// record's generated equality compares that by reference, so two runs producing identical
// trades would still come out unequal.
```

With no composite event left, every event is a flat record with value equality, and
`DeterminismTests.Describe` can go. That matters beyond tidiness: golden-file testing in phase 3c
rests on event streams comparing by value.

**Nothing is lost.** `OrdersMatched` carries `Symbol`, `Time`, `Price` and `Quantity`, and every
one of those is already on each `FillOrderConfirmed`.

## The trade id

A trade is one resting order matched against one aggressor - which is exactly what `ApplyTrade`
emits today, so the identity already exists and is simply unnamed. An auction printing three
pairs at one price is three trades, not one, and gets three ids.

Scope and issue it the way order ids are issued, because the reasoning transfers intact:

- **Per book.** `ExchangeOrderId` is per book, from a counter seeded by the session date, and the
  venue-wide identity of an order is the pair `(Instrument, ExchangeOrderId)`. A trade id follows:
  the pair is `(Instrument, TradeId)`. A shared venue-wide counter would make each book's ids
  depend on every other book's traffic and stop a book being reproducible from its own actions -
  the same argument `Order` already makes for order ids.
- **Its own counter, not the order counter.** An order id and a trade id may both read `5`;
  they are different namespaces, and interleaving them into one sequence would make both harder
  to read for no gain.
- **Seeded from the session date, forward only.** `Math.Max` against the seed, exactly as
  `_nextSequenceNumber` does, so a replay whose clock moves backwards cannot re-issue ids and a
  second session on one date continues rather than restarting.
- **`string`, like `ExchangeOrderId`.** Consistency wins here. If the per-access `ToString`
  allocation ever matters, both should move to a value type together - that is phase 4, behind a
  measurement, not something to decide here.

## The one consumer that gets harder

`TradeDataProducer` builds the public trade feed, and today reads `Symbol`, `Time`, `Price` and
`Quantity` off `OrdersMatched` - never `Fills`. It now has to derive one public print from two
private fills.

Emit when the trade id differs from the previous fill's. The two fills of a trade are emitted
adjacent, so this is an O(1) comparison against the last id seen and no set to maintain.

The alternative - print on the fill where `IsResting` - is the same one-liner and relies on
"exactly one resting fill per trade", which is structurally true today but would quietly break
if an implied trade ever spanned legs. De-duplicating on the identity of a trade says what is
meant, which is why the id is being added.

## Everything else in src

- `LevelDataProducer`, `FullBookDataProducer`: `case OrdersMatched matched:` + a loop over
  `matched.Fills` becomes `case FillOrderConfirmed fill:` with the loop body unchanged. Both
  carry a comment saying fills are "only ever nested inside `OrdersMatched.Fills`, never a
  top-level event in its own right" - which inverts.
- `OrderFlowSimulator`: the same shape, iterating fills to retire filled orders. Simpler after.
- `OrderBook.ApplyTrade`: emits two events instead of one wrapping two. Resting first, then
  aggressor, matching the current order within `Fills`, then the replenish events as now.

That is six references across five files. The engine change is small.

## The tests are the work

Roughly a hundred references across twenty-two files. Mechanical, but not uniformly so, and the
count of events an action produces changes - one `OrdersMatched` becomes two
`FillOrderConfirmed` - so anything asserting `events.Count` or indexing `events[2]` moves too.
`CreateOrderTests` alone has twenty-three references, largely index-based.

Suggested slicing, each independently reviewable:

1. **Add the trade id to `FillOrderConfirmed`**, populated, with `OrdersMatched` still in place.
   Nothing breaks; a test pins that both fills of a trade share an id and that two trades differ.
2. **Flatten the engine and the five `src` consumers.** Tests break in bulk here.
3. **Migrate the tests**, file by file.
4. **Delete `OrdersMatched` and `DeterminismTests.Describe`.**

Steps 1 and 2 could land together; 3 is the bulk and is best split across a few commits rather
than one wall of diff.

## Risk

The engine change is small and well understood. The test migration is a hundred edits, many of
them positional assertions that will compile and fail rather than fail to compile - and it
cannot be checked locally in the environment this was planned in, which has no .NET SDK and no
route to one. CI is the only verification available, so this should be expected to take a few
rounds and should not be squeezed into a single commit that is hard to bisect.

If the test migration is better done by someone who can run it locally, steps 1-2 are the part
worth having from here, and step 3 is honest mechanical work to hand over.

## Not in this change

- Removing `Order` from events. That is an allocation question, not a structural one, and phase 4
  says measure first. Flattening does not depend on it either way.
- Renaming `FillOrderConfirmed`. It is now a top-level private execution report and the name
  still fits; renaming would only add to an already large diff.
