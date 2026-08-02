namespace Circus;

// Which algorithm an instrument's continuous trading allocates under.
//
// Only continuous trading varies. An auction uncrosses at a single price whatever priority the
// rest of the day runs on, and a closed or halted book matches nothing at all - so pre-open, a
// pause, a close and a halt are the same phase whichever of these an instrument names.
//
// The instrument names one rather than carrying one: an algorithm instance holds run-scoped
// state - an auction's struck price, a pro-rata level's pending allocations - so it belongs to
// the single book running it rather than to the description of a contract.
public enum MatchingAlgorithm
{
    // Price, then time: the earliest order at the best price trades first. What most markets
    // run, and the default because an instrument that says nothing should get the ordinary one.
    PriceTime,

    // Price, then size: an aggressor's quantity is shared across the best level in proportion
    // to what each resting order still has left, so arriving first buys nothing over resting
    // large. CME runs it at the short end of the rates complex, where a tick is wide enough
    // that a place in a FIFO queue would be worth more than the trade.
    ProRata
}
