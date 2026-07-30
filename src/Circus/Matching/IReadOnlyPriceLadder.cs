namespace Circus.Matching;

// All anything outside Matcher is handed. Writes go through Matcher's own Rest/Unrest/Reprice,
// so the ladders it owns cannot be mutated from outside.
internal interface IReadOnlyPriceLadder
{
    bool TryGetBest(out long tick, out InternalOrder? firstOrder);

    IEnumerable<(long Tick, InternalOrder First, int Count)> EnumerateFromBest();
}
