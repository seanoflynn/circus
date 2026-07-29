namespace Circus.Sessions;

// One session's boundaries as times of day. A day's schedule is an ordered, non-overlapping
// list of these - a single continuous session for most products, two or more for one that
// breaks (a lunch recess, a separate evening session).
//
// All three times fall within the same day, so a session cannot span midnight. An overnight
// product would need boundaries that carry a day offset, which nothing here models yet.
public record TradingSession(TimeSpan PreOpen, TimeSpan Open, TimeSpan Close);
