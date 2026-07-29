namespace Circus.OrderBook;

// Appended rather than reordered, so the numeric value of an existing status never moves.
public enum OrderBookStatus
{
    PreOpen,
    Open,
    Closed,

    // Trading interrupted within a session and expected to resume: orders accumulate into an
    // auction and a quote keeps being published, but nothing matches. What a volatility band
    // breach moves the book to.
    Paused,

    // Trading suspended with no price discovery at all - no matching and no quote. What a
    // circuit breaker moves the book to.
    Halted
}
