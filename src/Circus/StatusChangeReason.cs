namespace Circus;

// Why the book changed status. Distinguishes the transitions a consumer would otherwise have to
// infer from context - a session opening looks nothing like a volatility pause on a feed, but
// both are just a status. Only what the book itself can tell apart: which restriction fired is
// not yet part of this, since the book sees a breach action rather than a named restriction.
public enum StatusChangeReason
{
    // Driven from outside - a session schedule, or an operator.
    Requested,

    // A prospective trade breached a price restriction.
    PriceRestriction,

    // A timed interruption ran its course and the book returned by itself.
    InterruptionElapsed
}
