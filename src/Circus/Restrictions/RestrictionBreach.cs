namespace Circus.Restrictions;

internal readonly record struct RestrictionBreach(RestrictionBreachAction Action, TimeSpan? ResumeAfter);
