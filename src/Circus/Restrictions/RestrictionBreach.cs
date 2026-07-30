namespace Circus.Restrictions;

// A breached Trade-scoped restriction: what it costs the book, and how long for. ResumeAfter
// null leaves the interruption open-ended, waiting for someone to end it explicitly - and for
// Block means nothing at all, since a limit does not interrupt anything to be resumed from.
internal readonly record struct RestrictionBreach(RestrictionBreachAction Action, TimeSpan? ResumeAfter);
