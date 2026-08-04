namespace Circus.Matching;

// All anything outside Matcher is handed. Writes go through Matcher's own Rest/Unrest/Reprice,
// so the ladders it owns cannot be mutated from outside.
internal interface IReadOnlyPriceLadder
{
    bool TryGetBest(out long tick, out InternalOrder? firstOrder);

    IEnumerable<(long Tick, InternalOrder First, int Count)> EnumerateFromBest();

    // Aggregated depth rather than orders - see PriceLadder for why the two are separate walks,
    // and why this fills a caller's list instead of returning a sequence.
    void CopyLevelsFromBest(int maxLevels, List<(long Tick, int Quantity, int Count)> into);
}
