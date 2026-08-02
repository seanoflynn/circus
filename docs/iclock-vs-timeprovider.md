# Should `IClock` be replaced with `TimeProvider`?

**Recommendation: no — keep `IClock`, and add a ~10 line `TimeProviderClock` adapter beside it.**

The BCL alternative is real and the project targets a framework that has it. But the swap is
a net loss here: it trades a `DateTime`-shaped seam that fits the domain for a
`DateTimeOffset`-shaped one that does not, and it cannot express a test the venue already
relies on. The interop benefit — the only genuine one — is available for a tenth of the cost
without changing anything.

## What exists today

`src/Circus/Time/` is 33 lines across three files:

| Type | Lines | Role |
| --- | --- | --- |
| `IClock` | 6 | One member: `DateTime GetCurrentTime()` |
| `SystemClock` | 9 | Returns `DateTime.UtcNow` |
| `ManualClock` | 18 | Holds a `DateTime`, `SetCurrentTime` moves it anywhere |

Two consumers in `src/`, and they are deliberately the only two:

- `LiveDriver` (`src/Circus/Sequencing/LiveDriver.cs:29`) — stamps arriving actions and
  advances the sequencer.
- `TimestampingOrderBook` (`src/Circus/TimestampingOrderBook.cs:23`) — stamps actions on the
  way into a book.

Both are documented as *the* boundary where wall-clock time enters. Everything downstream is a
pure function of actions that already carry their instant. That architecture is what makes
replay deterministic, and it is worth noting up front because it is also what makes
`TimeProvider` mostly redundant here.

Beyond `src/`: 27 test files, 34 `new ManualClock(...)` sites, 205 `SetCurrentTime` calls, and
two samples.

## The candidate

`System.TimeProvider` (BCL since .NET 8; this repo is `net10.0`, so it is available with no
package reference) exposes five things:

```
DateTimeOffset GetUtcNow()
TimeZoneInfo LocalTimeZone
long GetTimestamp() / TimestampFrequency
ITimer CreateTimer(...)
```

`TimeProvider.System` would replace `SystemClock`. `FakeTimeProvider` would replace
`ManualClock` — but it lives in `Microsoft.Extensions.TimeProvider.Testing`, a NuGet package,
not the BCL.

## Why not

### 1. The return type is wrong for this domain

`GetUtcNow()` returns `DateTimeOffset`. Circus is `DateTime` throughout: **119** `DateTime`
references in `src/` outside the `Time` namespace, and **zero** `DateTimeOffset` anywhere in
the repository. That includes the public shape of the library:

- All 16 event records carry `DateTime Time` (`src/Circus/Events/OrderBookEvent.cs`).
- `OrderBookAction.Time` (`src/Circus/Actions/OrderBookAction.cs:15`).
- `Order.CompletedTime`, `StatusChanged.ResumesAt`, `Replay(until:)`.
- The sequencer's priority tuple, `(DateTime Time, DispatchKind Kind, long Counter)`
  (`src/Circus/Sequencing/Sequencer.cs:39`).

That leaves two ways to adopt it, and both are bad:

**(a) Convert at the seam** — call `.UtcDateTime` in `LiveDriver` and `TimestampingOrderBook`.
Two lines, works today. But the result is a lossy adapter bolted onto a BCL type in order to
get back the type we started with. `IClock` already *is* that adapter, and it is smaller, has
no conversion, and states the intent in its signature.

**(b) Migrate the domain to `DateTimeOffset`** — a 119-site change, a breaking change to a
package already published at `0.7.0`, and 16 bytes per timestamp instead of 8. Circus
allocates an event per action and compares that tuple on every sequencer dispatch, so the
widening lands in the hot path. It buys nothing: a venue's matching engine has exactly one
timezone, and an offset it must never disagree about is a field that can only be wrong.

### 2. Four of the five members are dead weight

There is no `Task`, `async`, `Timer`, `Thread.Sleep`, or `CancellationToken` anywhere in
`src/` — verified across the whole library. Circus is synchronous by construction.

Most of `TimeProvider`'s value — and nearly all of the reason the BCL introduced it — is
`CreateTimer` and `Task.Delay`, so that code which *waits* can be tested without waiting.
Circus solved that problem a different and better way: `Sequencer.AdvanceTo(time)` takes the
instant as an argument, and `LiveDriver` is the single place a real clock is read. Determinism
is architectural here, not achieved by faking a timer. Adopting a five-member abstraction to
use one member, where the other four encode a concurrency model the library deliberately does
not have, is the wrong direction.

### 3. `FakeTimeProvider` cannot express a test that already exists

`LiveDriverTests.cs:145` — `Submit_AClockThatWentBackwards_IsRefusedRatherThanReorderingTheVenue`
— moves the clock from 12:00 to 11:59 to prove the sequencer refuses an NTP correction rather
than quietly reordering the venue. `LiveDriver`'s own header comment calls this out as
intended behaviour.

`ManualClock.SetCurrentTime` allows it. `FakeTimeProvider.SetUtcNow` **throws** when asked to
move backwards. Keeping that test means subclassing `TimeProvider` with a fake that permits
going back — which is `ManualClock` again, under a longer name and with a `DateTimeOffset` on
the front.

This is not an edge case to route around. A venue whose clock can jump backwards is the exact
scenario the sequencer's monotonicity check exists for, and the test suite should keep being
able to construct it.

### 4. The test constants would convert silently wrong

Every test timestamp is written as `new(2000, 1, 1, 12, 0, 0)` — `DateTimeKind.Unspecified`.
The implicit `DateTime` → `DateTimeOffset` conversion interprets `Unspecified` as **local
time**. So `SetUtcNow(Now2)` across 205 call sites would compile with no warning, pass on a
UTC CI runner, and shift every timestamp by the offset on a developer machine in any other
zone.

Fixing it properly means stamping `DateTimeKind.Utc` on every constant first. That is a
worthwhile tidy-up on its own merits, but it is a prerequisite this migration would introduce
and then depend on — a machine-dependent silent failure mode in exchange for deleting 33
lines.

### 5. It costs the project its dependency-free claim

The README states: *"There are no other dependencies."* `TimeProvider` itself is free, but the
test double is not — `Microsoft.Extensions.TimeProvider.Testing` would be a new package
reference. It is test-only and never flows to consumers of the `Circus` package, so this is
the weakest of the five objections. It is still a dependency, a servicing surface, and a
supply-chain entry, taken on to avoid maintaining an 18-line class that has needed no
maintenance.

## Why it is still worth doing something

One argument for `TimeProvider` survives all of the above: **interop**. A consumer hosting
Circus in ASP.NET Core already has a `TimeProvider` in DI — the framework registers one — and
may already fake it in their own tests. Today, handing it to `LiveDriver` means writing an
adapter themselves. Every consumer writes the same ten lines.

Ship those ten lines instead:

```csharp
namespace Circus.Time;

// Bridges a BCL TimeProvider into the seam the venue reads. A host that already resolves a
// TimeProvider - ASP.NET Core registers one - passes it here rather than writing this itself.
// The domain is DateTime and UTC throughout, so the offset is dropped on the way in.
public sealed class TimeProviderClock(TimeProvider timeProvider) : IClock
{
    private readonly TimeProvider _timeProvider =
        timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public DateTime GetCurrentTime() => _timeProvider.GetUtcNow().UtcDateTime;
}
```

That captures the whole benefit. No breaking change, no dependency, no test churn, no
`DateTimeOffset` in the domain, and `ManualClock` keeps its backwards jump.

## Plan

Small, and deliberately so.

1. **Add `src/Circus/Time/TimeProviderClock.cs`** as above. Header comment in the style of the
   surrounding files — say why the conversion is lossy on purpose.
2. **Add a test** in `tests/Circus.Tests/` asserting it reads through to the provider and
   returns `DateTimeKind.Utc`. Use a small local `TimeProvider` subclass rather than taking the
   `Microsoft.Extensions.TimeProvider.Testing` reference — one override, and the dependency
   claim in the README stays true.
3. **Note it in the README** feature list, which already has a `Time provider` line under
   Sessions.
4. **Change nothing else.** `IClock`, `SystemClock`, and `ManualClock` stay exactly as they
   are.

Not in scope, and each independently defensible if wanted later:

- Stamping `DateTimeKind.Utc` on the test constants. Worth doing, unrelated to this question,
  and better with the resulting diff in front of you.
- `SystemClock` delegating to `TimeProvider.System`. Pure churn — it would still return
  `DateTime.UtcNow`, through one more layer.

## Revisit this if

The recommendation is a function of the library's current shape, not a permanent verdict. It
should be reopened if any of these change:

- **Circus grows asynchrony** — a hosted pump, a timer-driven `Tick`, anything with `Task.Delay`.
  That is the case `TimeProvider` was built for, and it would immediately be worth more than
  `IClock`.
- **The domain moves to `DateTimeOffset`** for an unrelated reason (multi-venue support with
  genuinely distinct local sessions is the plausible one). Objection 1 disappears, and adopting
  `TimeProvider` would then be nearly free.
- **A major version bump** is planned for other breaking changes, making the public-API cost of
  objection 1(b) something the project is already paying.
